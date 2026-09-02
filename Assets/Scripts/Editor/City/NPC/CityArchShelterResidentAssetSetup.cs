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
    [InitializeOnLoad]
    public static class CityArchShelterResidentAssetSetup
    {
        public const string PlayerModelPath = "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        public const string SharedMaterialPath = "Assets/Player3D/Materials/Player3DLit.mat";
        public const string AnimationPath = "Assets/Pedestrians/Animations/NightlifeShelterResidents.fbx";
        public const string AnimationManifestPath = "Assets/Pedestrians/Animations/NightlifeShelterResidents.json";
        public const string ProviderPath = "Assets/Resources/City/CityArchShelterResidentProvider.asset";
        public const string StandingModelPath =
            "Assets/Pedestrians/Staged/Models/NightlifeShelterStandingResident3D.fbx";
        public const string StandingManifestPath =
            "Assets/Pedestrians/Staged/Models/NightlifeShelterStandingResident3D.json";
        public const string StandingPrefabPath =
            "Assets/Pedestrians/Staged/Prefabs/NightlifeShelterStandingResident3D.prefab";
        public const string StandingAtlasPath =
            "Assets/Pedestrians/Textures/NightlifeShelterStandingResident3DDetailAtlas.png";
        public const string SeatedModelPath =
            "Assets/Pedestrians/Staged/Models/NightlifeShelterSeatedResident3D.fbx";
        public const string SeatedManifestPath =
            "Assets/Pedestrians/Staged/Models/NightlifeShelterSeatedResident3D.json";
        public const string SeatedPrefabPath =
            "Assets/Pedestrians/Staged/Prefabs/NightlifeShelterSeatedResident3D.prefab";
        public const string SeatedAtlasPath =
            "Assets/Pedestrians/Textures/NightlifeShelterSeatedResident3DDetailAtlas.png";
        public const string SleepingModelPath =
            "Assets/Pedestrians/Staged/Models/NightlifeShelterSleepingResident3D.fbx";
        public const string SleepingManifestPath =
            "Assets/Pedestrians/Staged/Models/NightlifeShelterSleepingResident3D.json";
        public const string SleepingPrefabPath =
            "Assets/Pedestrians/Staged/Prefabs/NightlifeShelterSleepingResident3D.prefab";
        public const string SleepingAtlasPath =
            "Assets/Pedestrians/Textures/NightlifeShelterSleepingResident3DDetailAtlas.png";
        private const string Anatomy = "NpcHumanV2";
        private const int BoneCount = 31, Fps = 24, AtlasSize = 256;
        private const int MinimumTriangles = 1500, MaximumTriangles = 2300;
        private const float Height = 1.75f;
        private const float RestPelvisHeight = 0.835f;
        private const float PositionTolerance = 0.0001f,
            RotationTolerance = 0.02f;
        private const float ClipTolerance = 0.002f,
            ColorTolerance = 0.0001f, UvTolerance = 0.0001f;
        private static readonly RoleSpec[] Roles =
        {
            new RoleSpec(
                CityArchShelterResidentRole.StandingWarmer,
                "Nightlife shelter standing resident",
                "NightlifeShelterStandingResident3D",
                "nightlife_shelter_standing_resident_v2",
                StandingModelPath, StandingManifestPath,
                StandingPrefabPath, StandingAtlasPath,
                "ShelterStandingWarm", 8f),
            new RoleSpec(
                CityArchShelterResidentRole.SeatedWarmer,
                "Nightlife shelter seated resident",
                "NightlifeShelterSeatedResident3D",
                "nightlife_shelter_seated_resident_v2",
                SeatedModelPath, SeatedManifestPath,
                SeatedPrefabPath, SeatedAtlasPath,
                "ShelterSeatedWarm", 9f),
            new RoleSpec(
                CityArchShelterResidentRole.Sleeper,
                "Nightlife shelter sleeping resident",
                "NightlifeShelterSleepingResident3D",
                "nightlife_shelter_sleeping_resident_v2",
                SleepingModelPath, SleepingManifestPath,
                SleepingPrefabPath, SleepingAtlasPath,
                "ShelterSleeperBreath", 10f)
        };
        private static bool isBuilding;
        private static bool buildQueued;
        public static bool IsBuilding => isBuilding;
        static CityArchShelterResidentAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }
        [MenuItem("Bar Promenade/NPC Human V2/Build Arch Shelter Residents")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log("Arch-shelter resident prefabs and provider rebuilt.");
        }
        [MenuItem(
            "Bar Promenade/NPC Human V2/Validate Arch Shelter Residents")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("Arch-shelter resident asset contract is valid.");
        }
        public static bool SourcesExist()
        {
            return File.Exists(PlayerModelPath) &&
                   File.Exists(SharedMaterialPath) &&
                   File.Exists(AnimationPath) &&
                   File.Exists(AnimationManifestPath) &&
                   Roles.All(role => File.Exists(role.ModelPath) &&
                       File.Exists(role.ManifestPath) &&
                       File.Exists(role.AtlasPath));
        }
        public static bool IsOwnedModelPath(string path)
        {
            return !string.IsNullOrEmpty(path) && Roles.Any(role =>
                SamePath(path, role.ModelPath));
        }
        public static bool IsDetailAtlasPath(string path)
        {
            return !string.IsNullOrEmpty(path) && Roles.Any(role =>
                SamePath(path, role.AtlasPath));
        }
        public static bool IsOwnedSourcePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            // Hero V2 and Player3DLit are dependencies, not rebuild triggers.
            return SamePath(path, AnimationPath) ||
                   SamePath(path, AnimationManifestPath) ||
                   Roles.Any(role => SamePath(path, role.ModelPath) ||
                       SamePath(path, role.ManifestPath) ||
                       SamePath(path, role.AtlasPath));
        }
        public static bool TryGetClipLoopFlag(string name, out bool loop)
        {
            loop = Roles.Any(role => string.Equals(
                role.ClipName, name, StringComparison.Ordinal));
            return loop;
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
            RequireSources();
            isBuilding = true;
            try
            {
                EnsureFolder(ProviderPath);
                foreach (RoleSpec role in Roles)
                {
                    EnsureFolder(role.PrefabPath);
                }
                Import(PlayerModelPath);
                foreach (RoleSpec role in Roles)
                {
                    Import(role.AtlasPath);
                    Import(role.ManifestPath);
                    Import(role.ModelPath);
                }
                Import(AnimationManifestPath);
                Import(AnimationPath);
                AnimationManifest animations = LoadAnimations();
                Material material = AssetDatabase.LoadAssetAtPath<Material>(
                    SharedMaterialPath);
                if (material == null)
                {
                    throw new InvalidOperationException(
                        "Missing shared Player3DLit material.");
                }
                foreach (RoleSpec role in Roles)
                {
                    BuildPrefab(role, animations, material);
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
            RequireSources();
            AnimationManifest animations = LoadAnimations();
            foreach (RoleSpec role in Roles)
            {
                ValidateRole(role, animations);
            }
            ValidateProvider();
        }
        private static void BuildPrefab(
            RoleSpec role,
            AnimationManifest animations,
            Material material)
        {
            ModelManifest manifest = LoadModel(role);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
                role.ModelPath);
            Texture2D atlas = LoadAtlas(role);
            AnimationClip clip = LoadClip(role, animations);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not import {role.DisplayName}.");
            }
            var root = new GameObject(role.RootName);
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(source) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate {role.DisplayName}.");
                }
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.SetLocalPositionAndRotation(
                    Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
                model.transform.localScale = Vector3.one;
                Dictionary<string, Renderer> renderersByName =
                    IndexRenderers(model, role.DisplayName);
                Dictionary<string, Transform> transforms =
                    IndexTransforms(model, role.DisplayName);
                if (renderersByName.Count != manifest.mesh_count)
                {
                    throw new InvalidOperationException(
                        $"{role.DisplayName} renderer count is stale.");
                }
                var renderers = new List<Renderer>(manifest.parts.Length);
                var bindings =
                    new List<CityArchShelterResidentRendererBinding>(
                        manifest.parts.Length);
                foreach (Part sourcePart in manifest.parts)
                {
                    if (!renderersByName.TryGetValue(
                            sourcePart.name, out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            $"{role.DisplayName} is missing renderer " +
                            $"'{sourcePart.name}'.");
                    }
                    renderer.sharedMaterials = new[] { material };
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.motionVectorGenerationMode =
                        MotionVectorGenerationMode.Object;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        skinned.updateWhenOffscreen = true;
                    }
                    renderers.Add(renderer);
                    bindings.Add(
                        new CityArchShelterResidentRendererBinding(
                            renderer, ParseColor(sourcePart.base_color),
                            !string.IsNullOrEmpty(
                                sourcePart.atlas_region)));
                }
                Animator animator = RequireAnimator(model, role.DisplayName);
                Transform head = RequireTransform(
                    transforms, "head", role.DisplayName);
                Transform pelvis = RequireTransform(
                    transforms, "pelvis", role.DisplayName);
                Transform leftFoot = RequireTransform(
                    transforms, "foot.L", role.DisplayName);
                Transform rightFoot = RequireTransform(
                    transforms, "foot.R", role.DisplayName);
                Renderer[] rendererArray = renderers.ToArray();
                Bounds bounds = CalculateBounds(root.transform, rendererArray);
                ValidateGeometry(role, manifest, bounds, rendererArray);
                ValidateUvs(manifest.texture_bindings[0], renderersByName);
                CityArchShelterResidentAssetRegistry registry =
                    root.AddComponent<
                        CityArchShelterResidentAssetRegistry>();
                registry.Configure(
                    animator, role.Role, clip, model.transform,
                    bindings.ToArray(), head, pelvis, leftFoot, rightFoot,
                    atlas, bounds, manifest.triangle_count,
                    manifest.generator_version, manifest.design_id,
                    manifest.build_signature);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    root, role.PrefabPath, out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save '{role.PrefabPath}'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        private static Animator RequireAnimator(GameObject model, string label)
        {
            Animator[] animators =
                model.GetComponentsInChildren<Animator>(true);
            Animator animator = animators.Length == 0
                ? model.AddComponent<Animator>()
                : animators.Length == 1 ? animators[0] : null;
            Avatar avatar = FindAvatar();
            if (animator == null)
            {
                throw new InvalidOperationException(
                    $"{label} contains more than one Animator.");
            }
            if (animator.avatar == null)
            {
                animator.avatar = avatar;
            }
            if (avatar == null || !avatar.isValid || animator.avatar != avatar)
            {
                throw new InvalidOperationException(
                    $"{label} does not use Hero/NPC V2 Avatar.");
            }
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return animator;
        }
        private static void BindProvider()
        {
            CityArchShelterResidentProvider provider =
                AssetDatabase.LoadAssetAtPath<
                    CityArchShelterResidentProvider>(ProviderPath);
            if (provider == null)
            {
                provider = ScriptableObject.CreateInstance<
                    CityArchShelterResidentProvider>();
                AssetDatabase.CreateAsset(provider, ProviderPath);
            }
            provider.Configure(
                LoadPrefab(Roles[0]), LoadPrefab(Roles[1]),
                LoadPrefab(Roles[2]));
            EditorUtility.SetDirty(provider);
        }
        private static void ValidateRole(
            RoleSpec role,
            AnimationManifest animations)
        {
            ModelManifest manifest = LoadModel(role);
            ValidateImportedModel(role, manifest);
            Texture2D atlas = LoadAtlas(role);
            GameObject prefab = LoadPrefab(role);
            CityArchShelterResidentAssetRegistry registry =
                prefab.GetComponent<
                    CityArchShelterResidentAssetRegistry>();
            if (registry == null || registry.gameObject != prefab)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} needs one root registry.");
            }
            Avatar avatar = FindAvatar();
            Animator[] animators =
                prefab.GetComponentsInChildren<Animator>(true);
            if (animators.Length != 1 || registry.Animator != animators[0] ||
                registry.Animator.avatar != avatar ||
                registry.Animator.runtimeAnimatorController != null ||
                registry.Animator.applyRootMotion ||
                registry.Animator.cullingMode !=
                    AnimatorCullingMode.CullUpdateTransforms)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} Animator contract is stale.");
            }
            if (registry.ModelRoot == null ||
                registry.ModelRoot.parent != prefab.transform ||
                registry.ModelRoot.localPosition != Vector3.zero ||
                Quaternion.Angle(registry.ModelRoot.localRotation,
                    Quaternion.Euler(0f, 180f, 0f)) > RotationTolerance ||
                registry.ModelRoot.localScale != Vector3.one)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} staged model transform is stale.");
            }
            if (registry.Role != role.Role || registry.DetailAtlas != atlas ||
                registry.TriangleCount != manifest.triangle_count ||
                registry.GeneratorVersion != manifest.generator_version ||
                registry.DesignId != manifest.design_id ||
                registry.BuildSignature != manifest.build_signature)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} registry metadata is stale.");
            }
            ValidateClip(registry.IdleClip, role, animations);
            ValidateAnchors(role, registry);
            ValidateBindings(role, prefab, registry, manifest);
            Bounds actualBounds = CalculateBounds(
                prefab.transform,
                prefab.GetComponentsInChildren<Renderer>(true));
            if (Vector3.Distance(
                    registry.LocalBounds.center,
                    actualBounds.center) > 0.0005f ||
                Vector3.Distance(
                    registry.LocalBounds.size,
                    actualBounds.size) > 0.0005f)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} registry bounds are stale.");
            }
            ValidatePassive(role, prefab, registry);
        }
        private static void ValidateAnchors(
            RoleSpec role,
            CityArchShelterResidentAssetRegistry registry)
        {
            Transform[] actual =
            {
                registry.Head, registry.Pelvis,
                registry.LeftFoot, registry.RightFoot
            };
            string[] names = { "head", "pelvis", "foot.L", "foot.R" };
            for (int index = 0; index < names.Length; index++)
            {
                if (actual[index] == null ||
                    !actual[index].IsChildOf(registry.ModelRoot) ||
                    actual[index].name != names[index])
                {
                    throw new InvalidOperationException(
                        $"{role.DisplayName} anchor '{names[index]}' " +
                        "is missing or stale.");
                }
            }
        }
        private static void ValidateBindings(
            RoleSpec role,
            GameObject prefab,
            CityArchShelterResidentAssetRegistry registry,
            ModelManifest manifest)
        {
            Renderer[] renderers =
                prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != manifest.mesh_count ||
                registry.RendererBindings.Count != manifest.mesh_count)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} renderer count is stale.");
            }
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                SharedMaterialPath);
            var seen = new HashSet<Renderer>();
            var byName = new Dictionary<string, Renderer>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.parts.Length; index++)
            {
                Part source = manifest.parts[index];
                CityArchShelterResidentRendererBinding binding =
                    registry.RendererBindings[index];
                if (binding == null || binding.Renderer == null ||
                    !seen.Add(binding.Renderer) ||
                    !byName.TryAdd(binding.Renderer.name, binding.Renderer) ||
                    binding.Renderer.name != source.name ||
                    !SameColor(binding.Color, ParseColor(source.base_color)) ||
                    binding.UsesDetailAtlas !=
                        !string.IsNullOrEmpty(source.atlas_region) ||
                    binding.Renderer.sharedMaterials.Length != 1 ||
                    binding.Renderer.sharedMaterial != material ||
                    binding.Renderer.shadowCastingMode != ShadowCastingMode.On ||
                    !binding.Renderer.receiveShadows)
                {
                    throw new InvalidOperationException(
                        $"{role.DisplayName} binding {index} is stale.");
                }
            }
            if (CountTriangles(renderers) != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} triangle count is stale.");
            }
            ValidateUvs(manifest.texture_bindings[0], byName);
        }
        private static void ValidatePassive(
            RoleSpec role,
            GameObject prefab,
            CityArchShelterResidentAssetRegistry registry)
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
                    $"{role.DisplayName} prefab must remain passive.");
            }
            MonoBehaviour[] behaviours =
                prefab.GetComponentsInChildren<MonoBehaviour>(true);
            if (behaviours.Length != 1 || behaviours[0] != registry)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} may carry only its registry.");
            }
        }
        private static void ValidateProvider()
        {
            CityArchShelterResidentProvider provider =
                AssetDatabase.LoadAssetAtPath<
                    CityArchShelterResidentProvider>(ProviderPath);
            if (provider == null)
            {
                throw new InvalidOperationException(
                    "Missing arch-shelter resident provider.");
            }
            provider.ValidateOrThrow();
            GameObject[] actual =
            {
                provider.StandingPrefab,
                provider.SeatedPrefab,
                provider.SleeperPrefab
            };
            for (int index = 0; index < Roles.Length; index++)
            {
                GameObject expected = LoadPrefab(Roles[index]);
                if (actual[index] != expected ||
                    provider.GetPrefab(Roles[index].Role) != expected ||
                    AssetDatabase.GetAssetPath(actual[index]).IndexOf(
                        "/Resources/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidOperationException(
                        "Provider binding is stale or Resources-resident.");
                }
            }
            if (actual.Distinct().Count() != Roles.Length)
            {
                throw new InvalidOperationException(
                    "Every shelter role needs a distinct staged prefab.");
            }
        }
        private static ModelManifest LoadModel(RoleSpec role)
        {
            if (!role.ModelPath.StartsWith(
                    "Assets/Pedestrians/Staged/Models/",
                    StringComparison.OrdinalIgnoreCase) ||
                !role.PrefabPath.StartsWith(
                    "Assets/Pedestrians/Staged/Prefabs/",
                    StringComparison.OrdinalIgnoreCase) ||
                role.PrefabPath.IndexOf(
                    "/Resources/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                CityPedestrianResources.TryGetArchetype(role.DesignId, out _))
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} is not isolated from the pool.");
            }
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(
                role.ManifestPath);
            ModelManifest manifest = source == null
                ? null
                : JsonUtility.FromJson<ModelManifest>(source.text);
            if (manifest == null || manifest.parts == null ||
                manifest.bones == null || manifest.animations == null ||
                manifest.shared_clips == null ||
                manifest.triangle_budget == null ||
                manifest.triangle_budget.Length != 2)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} manifest is malformed.");
            }
            if (manifest.design_id != role.DesignId ||
                manifest.anatomy_standard != Anatomy ||
                manifest.pose != "apose" || manifest.forward_axis != "-Y" ||
                manifest.anatomical_left_axis != "+X" ||
                Mathf.Abs(manifest.height_m - Height) > PositionTolerance ||
                Mathf.Abs(manifest.rest_pelvis_height_m - RestPelvisHeight) >
                    PositionTolerance ||
                manifest.bones.Length != BoneCount ||
                manifest.mesh_count != manifest.parts.Length ||
                manifest.triangle_count < MinimumTriangles ||
                manifest.triangle_count > MaximumTriangles ||
                manifest.triangle_budget[0] < MinimumTriangles ||
                manifest.triangle_budget[1] > MaximumTriangles ||
                manifest.triangle_budget[0] > manifest.triangle_count ||
                manifest.triangle_budget[1] < manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} anatomy or geometry is invalid.");
            }
            if (!manifest.staged || manifest.pool_eligible ||
                manifest.emissive || manifest.colliders ||
                manifest.animation_count != 0 ||
                manifest.animations.Length != 0 ||
                manifest.material_asset != SharedMaterialPath ||
                manifest.shared_animation_source != AnimationPath ||
                !manifest.shared_clips.SequenceEqual(
                    new[] { role.ClipName }, StringComparer.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.generator_version) ||
                manifest.build_signature?.Length != 64 ||
                (manifest.signature_effects ?? Array.Empty<string>()).Length !=
                    0 ||
                (manifest.rig_anchors ?? Array.Empty<RigAnchor>()).Length != 0)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} staged/source contract is invalid.");
            }
            HashSet<string> partNames = ValidateHierarchy(role, manifest);
            ValidateTexture(role, manifest, partNames);
            return manifest;
        }
        private static HashSet<string> ValidateHierarchy(
            RoleSpec role,
            ModelManifest manifest)
        {
            var bones = new HashSet<string>(StringComparer.Ordinal);
            foreach (Bone bone in manifest.bones)
            {
                if (bone == null || string.IsNullOrEmpty(bone.name) ||
                    !bones.Add(bone.name))
                {
                    throw new InvalidOperationException(
                        $"{role.DisplayName} has duplicate/missing bones.");
                }
            }
            var parts = new HashSet<string>(StringComparer.Ordinal);
            foreach (Part part in manifest.parts)
            {
                if (part == null || string.IsNullOrEmpty(part.name) ||
                    string.IsNullOrEmpty(part.role) ||
                    string.IsNullOrEmpty(part.palette_name) ||
                    part.base_color == null || part.base_color.Length != 4 ||
                    part.base_color.Any(value => value < 0f || value > 1f) ||
                    !parts.Add(part.name) || !bones.Contains(part.bone))
                {
                    throw new InvalidOperationException(
                        $"{role.DisplayName} has an invalid mesh binding.");
                }
            }
            return parts;
        }
        private static void ValidateTexture(
            RoleSpec role,
            ModelManifest manifest,
            HashSet<string> parts)
        {
            TextureBinding[] bindings =
                manifest.texture_bindings ?? Array.Empty<TextureBinding>();
            if (bindings.Length != 1 || bindings[0] == null)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} needs one detail atlas.");
            }
            TextureBinding binding = bindings[0];
            if (binding.texture_asset != role.AtlasPath ||
                binding.width_px != AtlasSize ||
                binding.height_px != AtlasSize ||
                binding.materials == null || binding.materials.Length != 0 ||
                binding.shader_property != "_BaseMap" ||
                binding.color_space != "sRGB" ||
                binding.filter_mode != "Point" ||
                binding.wrap_mode != "Clamp" || binding.mipmaps ||
                binding.compression != "Uncompressed" ||
                binding.uv_channel != 0 ||
                binding.uv_origin != "bottom_left" ||
                binding.uv_safe_inset_px != 1 ||
                binding.material_tint_hex != "FFFFFF" ||
                binding.tint_source != "renderer_palette" ||
                binding.sha256?.Length != 64 ||
                !string.Equals(binding.sha256, FileSha(role.AtlasPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} atlas contract is invalid.");
            }
            TextureRegion[] regions =
                binding.regions ?? Array.Empty<TextureRegion>();
            var regionNames = new HashSet<string>(StringComparer.Ordinal);
            var byRenderer = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (TextureRegion region in regions)
            {
                if (region == null || string.IsNullOrEmpty(region.name) ||
                    !regionNames.Add(region.name) ||
                    !parts.Contains(region.renderer) ||
                    !byRenderer.TryAdd(region.renderer, region.name) ||
                    region.width_px <= 2 || region.height_px <= 2 ||
                    region.x_px < 0 || region.y_px < 0 ||
                    region.x_px + region.width_px > AtlasSize ||
                    region.y_px + region.height_px > AtlasSize)
                {
                    throw new InvalidOperationException(
                        $"{role.DisplayName} has an invalid atlas region.");
                }
            }
            if (regions.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} atlas has no regions.");
            }
            foreach (Part part in manifest.parts)
            {
                byRenderer.TryGetValue(part.name, out string expected);
                if ((part.atlas_region ?? string.Empty) !=
                    (expected ?? string.Empty))
                {
                    throw new InvalidOperationException(
                        $"{role.DisplayName} atlas/part mapping is stale.");
                }
            }
        }
        private static AnimationManifest LoadAnimations()
        {
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(
                AnimationManifestPath);
            AnimationManifest manifest = source == null
                ? null
                : JsonUtility.FromJson<AnimationManifest>(source.text);
            if (manifest == null || manifest.clips == null ||
                manifest.fps != Fps || manifest.bone_count != BoneCount ||
                manifest.mesh_count != 0 ||
                manifest.clip_count != Roles.Length ||
                manifest.clips.Length != Roles.Length || manifest.root_motion ||
                manifest.anatomy_standard != Anatomy ||
                Mathf.Abs(manifest.rest_pelvis_height_m - RestPelvisHeight) >
                    PositionTolerance ||
                string.IsNullOrWhiteSpace(manifest.skeleton_source) ||
                string.IsNullOrWhiteSpace(manifest.generator_version) ||
                manifest.build_signature?.Length != 64)
            {
                throw new InvalidOperationException(
                    "Shelter animation manifest is invalid.");
            }
            for (int index = 0; index < Roles.Length; index++)
            {
                RoleSpec role = Roles[index];
                Clip sourceClip = manifest.clips[index];
                if (sourceClip == null || sourceClip.name != role.ClipName ||
                    sourceClip.archetype != role.DesignId ||
                    Mathf.Abs(sourceClip.duration_seconds - role.Duration) >
                        ClipTolerance || sourceClip.frame_start != 0 ||
                    sourceClip.frame_end != Mathf.RoundToInt(role.Duration * Fps) ||
                    !sourceClip.loop || sourceClip.one_shot ||
                    !sourceClip.in_place ||
                    sourceClip.keyed_bone_count != BoneCount ||
                    string.IsNullOrWhiteSpace(sourceClip.authored_posture) ||
                    string.IsNullOrWhiteSpace(sourceClip.gait) ||
                    Mathf.Abs(sourceClip.loop_max_error) > PositionTolerance ||
                    sourceClip.root_translation_range_m == null ||
                    sourceClip.root_translation_range_m.Length != 3 ||
                    sourceClip.root_translation_range_m.Any(value =>
                        Mathf.Abs(value) > PositionTolerance))
                {
                    throw new InvalidOperationException(
                        $"Shelter clip contract {index} is invalid.");
                }
                ValidateFootprint(
                    sourceClip,
                    role.Role == CityArchShelterResidentRole.Sleeper);
            }
            ValidateImportedAnimation();
            return manifest;
        }
        private static void ValidateFootprint(Clip clip, bool sleeper)
        {
            float[] min = clip.animated_local_xz_min_m;
            float[] max = clip.animated_local_xz_max_m;
            float[] size = clip.animated_local_xz_size_m;
            if (!IsFinitePair(min) || !IsFinitePair(max) ||
                !IsFinitePair(size))
            {
                throw new InvalidOperationException(
                    $"Shelter clip '{clip.name}' has no sampled footprint.");
            }
            for (int axis = 0; axis < 2; axis++)
            {
                if (max[axis] < min[axis] || size[axis] <= 0f ||
                    Mathf.Abs(size[axis] - (max[axis] - min[axis])) >
                        PositionTolerance)
                {
                    throw new InvalidOperationException(
                        $"Shelter clip '{clip.name}' footprint is stale.");
                }
            }
            if (!sleeper)
            {
                if (clip.mattress_footprint_m != null || clip.mattress_used_half_extents_m != null ||
                    clip.mattress_clearance_m != null || clip.animated_mattress_xz_min_m != null ||
                    clip.animated_mattress_xz_max_m != null ||
                    Mathf.Abs(clip.mattress_yaw_degrees) > PositionTolerance)
                {
                    throw new InvalidOperationException(
                        $"Shelter clip '{clip.name}' has sleeper metadata.");
                }
                return;
            }
            float[] footprint = clip.mattress_footprint_m,
                used = clip.mattress_used_half_extents_m,
                clearance = clip.mattress_clearance_m;
            float[] mattressMin = clip.animated_mattress_xz_min_m,
                mattressMax = clip.animated_mattress_xz_max_m;
            if (!IsFinitePair(footprint) || !IsFinitePair(used) ||
                !IsFinitePair(clearance) || !IsFinitePair(mattressMin) ||
                !IsFinitePair(mattressMax) ||
                Mathf.Abs(footprint[0] - CityArchShelterPlanner.BeddingMattressLength) > PositionTolerance ||
                Mathf.Abs(footprint[1] - CityArchShelterPlanner.BeddingMattressWidth) > PositionTolerance ||
                Mathf.Abs(clip.mattress_yaw_degrees) > PositionTolerance)
            {
                throw new InvalidOperationException(
                    "Sleeper mattress footprint metadata is invalid.");
            }
            for (int axis = 0; axis < 2; axis++)
            {
                float half = footprint[axis] * 0.5f;
                float measured = Mathf.Max(Mathf.Abs(mattressMin[axis]),
                    Mathf.Abs(mattressMax[axis]));
                if (used[axis] < 0f || used[axis] > half + 0.0005f ||
                    Mathf.Abs(used[axis] - measured) > PositionTolerance ||
                    clearance[axis] < -0.0005f ||
                    Mathf.Abs(clearance[axis] - (half - used[axis])) > 0.0005f)
                {
                    throw new InvalidOperationException(
                        "Sleeper animation exceeds its mattress footprint.");
                }
            }
        }
        private static bool IsFinitePair(float[] values)
        {
            return values != null && values.Length == 2 &&
                values.All(value => !float.IsNaN(value) &&
                    !float.IsInfinity(value));
        }
        private static void ValidateImportedAnimation()
        {
            Avatar avatar = FindAvatar();
            ModelImporter importer =
                AssetImporter.GetAtPath(AnimationPath) as ModelImporter;
            AnimationClip[] clips = ImportedClips();
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
                AnimationPath);
            if (avatar == null || !avatar.isValid || importer == null ||
                !importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.Generic ||
                importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther ||
                importer.sourceAvatar != avatar ||
                importer.materialImportMode != ModelImporterMaterialImportMode.None ||
                clips.Length != Roles.Length || source == null ||
                source.GetComponentsInChildren<Renderer>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Imported shelter animation bank is invalid.");
            }
            Transform root = RequireTransform(
                IndexTransforms(source, "shelter animation"),
                "root", "shelter animation");
            if (root.GetComponentsInChildren<Transform>(true).Length != BoneCount)
            {
                throw new InvalidOperationException(
                    "Shelter animation bank has the wrong skeleton.");
            }
        }
        private static void ValidateImportedModel(
            RoleSpec role,
            ModelManifest manifest)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(
                role.ModelPath);
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(
                PlayerModelPath);
            if (model == null || player == null)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} or Hero V2 failed to import.");
            }
            Dictionary<string, Transform> modelTransforms =
                IndexTransforms(model, role.DisplayName);
            Dictionary<string, Transform> playerTransforms =
                IndexTransforms(player, "Hero V2");
            foreach (Bone bone in manifest.bones)
            {
                Transform actual = RequireTransform(
                    modelTransforms, bone.name, role.DisplayName);
                Transform expected = RequireTransform(
                    playerTransforms, bone.name, "Hero V2");
                string parent = string.IsNullOrEmpty(bone.parent)
                    ? "RIG_Player" : bone.parent;
                if (actual.parent?.name != parent ||
                    expected.parent?.name != parent ||
                    Vector3.Distance(actual.localPosition,
                        expected.localPosition) > PositionTolerance ||
                    Quaternion.Angle(actual.localRotation,
                        expected.localRotation) > RotationTolerance ||
                    Vector3.Distance(actual.localScale,
                        expected.localScale) > PositionTolerance)
                {
                    throw new InvalidOperationException(
                        $"{role.DisplayName} bone '{bone.name}' is stale.");
                }
            }
            Transform rig = RequireTransform(
                modelTransforms, "root", role.DisplayName);
            Renderer[] renderers =
                model.GetComponentsInChildren<Renderer>(true);
            ModelImporter importer = AssetImporter.GetAtPath(
                role.ModelPath) as ModelImporter;
            Avatar avatar = FindAvatar();
            bool hasClips = AssetDatabase.LoadAllAssetsAtPath(role.ModelPath)
                .OfType<AnimationClip>().Any(clip =>
                    !clip.name.StartsWith("__preview__",
                        StringComparison.Ordinal));
            if (rig.GetComponentsInChildren<Transform>(true).Length != BoneCount ||
                renderers.Length != manifest.mesh_count ||
                CountTriangles(renderers) != manifest.triangle_count || hasClips ||
                avatar == null || !avatar.isValid || importer == null ||
                importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.Generic ||
                importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther ||
                importer.sourceAvatar != avatar ||
                importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                throw new InvalidOperationException(
                    $"Imported {role.DisplayName} contract is invalid.");
            }
        }
        private static Texture2D LoadAtlas(RoleSpec role)
        {
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(
                role.AtlasPath);
            TextureImporter importer =
                AssetImporter.GetAtPath(role.AtlasPath) as TextureImporter;
            if (atlas == null || atlas.width != AtlasSize ||
                atlas.height != AtlasSize || atlas.filterMode != FilterMode.Point ||
                atlas.wrapMode != TextureWrapMode.Clamp || atlas.mipmapCount != 1 ||
                importer == null || !importer.sRGBTexture || importer.isReadable ||
                importer.filterMode != FilterMode.Point ||
                importer.wrapMode != TextureWrapMode.Clamp ||
                importer.mipmapEnabled || importer.streamingMipmaps ||
                importer.maxTextureSize != AtlasSize ||
                importer.textureCompression !=
                    TextureImporterCompression.Uncompressed ||
                importer.alphaIsTransparency)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} atlas import contract is invalid.");
            }
            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");
            if (!standalone.overridden ||
                standalone.maxTextureSize != AtlasSize ||
                standalone.textureCompression !=
                    TextureImporterCompression.Uncompressed ||
                standalone.crunchedCompression)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} atlas is compressed on Standalone.");
            }
            return atlas;
        }
        private static AnimationClip LoadClip(
            RoleSpec role,
            AnimationManifest manifest)
        {
            AnimationClip clip = ImportedClips().FirstOrDefault(candidate =>
                NormalizeClip(candidate.name) == role.ClipName);
            ValidateClip(clip, role, manifest);
            return clip;
        }
        private static void ValidateClip(
            AnimationClip clip,
            RoleSpec role,
            AnimationManifest manifest)
        {
            Clip source = manifest.clips.FirstOrDefault(candidate =>
                candidate != null && candidate.name == role.ClipName);
            AnimationClipSettings settings = clip == null
                ? null : AnimationUtility.GetAnimationClipSettings(clip);
            if (clip == null || source == null ||
                NormalizeClip(clip.name) != role.ClipName ||
                Mathf.Abs(clip.length - role.Duration) > ClipTolerance ||
                settings == null || !settings.loopTime || !settings.loopBlend ||
                AnimationUtility.GetAnimationEvents(clip).Length != 0)
            {
                throw new InvalidOperationException(
                    $"Imported clip '{role.ClipName}' is invalid.");
            }
        }
        private static void ValidateGeometry(
            RoleSpec role,
            ModelManifest manifest,
            Bounds bounds,
            Renderer[] renderers)
        {
            if (Mathf.Abs(bounds.size.y - manifest.height_m) > 0.035f ||
                Mathf.Abs(bounds.min.y) > 0.025f ||
                CountTriangles(renderers) != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    $"{role.DisplayName} bounds/triangles are invalid.");
            }
        }
        private static void ValidateUvs(
            TextureBinding binding,
            IReadOnlyDictionary<string, Renderer> renderers)
        {
            foreach (TextureRegion region in binding.regions)
            {
                if (!renderers.TryGetValue(region.renderer, out Renderer renderer))
                {
                    throw new InvalidOperationException(
                        $"Missing textured renderer '{region.renderer}'.");
                }
                Mesh mesh = RendererMesh(renderer);
                Vector2[] uv = mesh?.uv;
                if (mesh == null || uv == null || uv.Length != mesh.vertexCount ||
                    uv.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{region.renderer}' has no valid UV0.");
                }
                Vector2 min = new Vector2(
                    (float)(region.x_px + binding.uv_safe_inset_px) /
                    binding.width_px,
                    (float)(region.y_px + binding.uv_safe_inset_px) /
                    binding.height_px);
                Vector2 max = new Vector2(
                    (float)(region.x_px + region.width_px -
                            binding.uv_safe_inset_px) / binding.width_px,
                    (float)(region.y_px + region.height_px -
                            binding.uv_safe_inset_px) / binding.height_px);
                Vector2 observedMin = uv[0];
                Vector2 observedMax = uv[0];
                foreach (Vector2 point in uv)
                {
                    if (point.x < min.x - UvTolerance ||
                        point.x > max.x + UvTolerance ||
                        point.y < min.y - UvTolerance ||
                        point.y > max.y + UvTolerance)
                    {
                        throw new InvalidOperationException(
                            $"'{region.renderer}' UV lies outside " +
                            $"'{region.name}'.");
                    }
                    observedMin = Vector2.Min(observedMin, point);
                    observedMax = Vector2.Max(observedMax, point);
                }
                if (observedMax.x - observedMin.x <= UvTolerance ||
                    observedMax.y - observedMin.y <= UvTolerance)
                {
                    throw new InvalidOperationException(
                        $"'{region.renderer}' has degenerate atlas UV0.");
                }
            }
        }
        private static Bounds CalculateBounds(
            Transform root,
            IReadOnlyList<Renderer> renderers)
        {
            Bounds result = default;
            bool initialized = false;
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = RendererMesh(renderer);
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.name}' has no mesh.");
                }
                Matrix4x4 toRoot =
                    root.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = toRoot.MultiplyPoint3x4(new Vector3(
                        (corner & 1) == 0 ? mesh.bounds.min.x : mesh.bounds.max.x,
                        (corner & 2) == 0 ? mesh.bounds.min.y : mesh.bounds.max.y,
                        (corner & 4) == 0 ? mesh.bounds.min.z : mesh.bounds.max.z));
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
                throw new InvalidOperationException("Resident has no renderers.");
            }
            return result;
        }
        private static int CountTriangles(IReadOnlyList<Renderer> renderers)
        {
            int count = 0;
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = RendererMesh(renderer);
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.name}' has no mesh.");
                }
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    count += (int)(mesh.GetIndexCount(subMesh) / 3);
                }
            }
            return count;
        }
        private static Mesh RendererMesh(Renderer renderer)
        {
            return renderer is SkinnedMeshRenderer skinned
                ? skinned.sharedMesh
                : renderer.GetComponent<MeshFilter>()?.sharedMesh;
        }
        private static Dictionary<string, Transform> IndexTransforms(
            GameObject root,
            string label)
        {
            var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
            {
                if (!result.TryAdd(item.name, item))
                {
                    throw new InvalidOperationException(
                        $"{label} duplicates transform '{item.name}'.");
                }
            }
            return result;
        }
        private static Dictionary<string, Renderer> IndexRenderers(
            GameObject root,
            string label)
        {
            var result = new Dictionary<string, Renderer>(StringComparer.Ordinal);
            foreach (Renderer item in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!result.TryAdd(item.name, item))
                {
                    throw new InvalidOperationException(
                        $"{label} duplicates renderer '{item.name}'.");
                }
            }
            return result;
        }
        private static Transform RequireTransform(
            IReadOnlyDictionary<string, Transform> transforms,
            string name,
            string label)
        {
            if (transforms.TryGetValue(name, out Transform result) && result != null)
            {
                return result;
            }
            throw new InvalidOperationException(
                $"{label} is missing transform '{name}'.");
        }
        private static GameObject LoadPrefab(RoleSpec role)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                role.PrefabPath);
            return prefab != null
                ? prefab
                : throw new InvalidOperationException(
                    $"Missing prefab '{role.PrefabPath}'.");
        }
        private static Avatar FindAvatar()
        {
            return AssetDatabase.LoadAllAssetsAtPath(PlayerModelPath)
                .OfType<Avatar>().FirstOrDefault();
        }
        private static AnimationClip[] ImportedClips()
        {
            return AssetDatabase.LoadAllAssetsAtPath(AnimationPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith(
                    "__preview__", StringComparison.Ordinal)).ToArray();
        }
        private static Color ParseColor(float[] rgba)
        {
            return new Color(rgba[0], rgba[1], rgba[2], rgba[3]);
        }
        private static bool SameColor(Color left, Color right)
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
                .Replace("-", string.Empty).ToLowerInvariant();
        }
        private static string NormalizeClip(string name)
        {
            int separator = name?.LastIndexOf('|') ?? -1;
            return separator >= 0 && separator + 1 < name.Length
                ? name.Substring(separator + 1) : name;
        }
        private static bool SamePath(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
        private static void RequireSources()
        {
            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "Arch-shelter resident sources are incomplete.");
            }
        }
        private static void Import(string path)
        {
            AssetDatabase.ImportAsset(path,
                ImportAssetOptions.ForceUpdate |
                ImportAssetOptions.ForceSynchronousImport);
        }
        private static void EnsureFolder(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
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
                Debug.LogError("Shelter resident validation failed: " + exception);
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
                Debug.LogError("Shelter resident build failed: " + exception);
            }
        }
        private sealed class RoleSpec
        {
            public RoleSpec(
                CityArchShelterResidentRole role, string displayName,
                string rootName, string designId, string modelPath,
                string manifestPath, string prefabPath, string atlasPath,
                string clipName, float duration)
            {
                Role = role;
                DisplayName = displayName;
                RootName = rootName;
                DesignId = designId;
                ModelPath = modelPath;
                ManifestPath = manifestPath;
                PrefabPath = prefabPath;
                AtlasPath = atlasPath;
                ClipName = clipName;
                Duration = duration;
            }
            public CityArchShelterResidentRole Role { get; }
            public string DisplayName { get; }
            public string RootName { get; }
            public string DesignId { get; }
            public string ModelPath { get; }
            public string ManifestPath { get; }
            public string PrefabPath { get; }
            public string AtlasPath { get; }
            public string ClipName { get; }
            public float Duration { get; }
        }
        [Serializable]
        private sealed class ModelManifest
        {
            public string generator_version, design_id, anatomy_standard;
            public float rest_pelvis_height_m, height_m;
            public string pose, forward_axis, anatomical_left_axis;
            public int mesh_count, triangle_count, animation_count;
            public int[] triangle_budget;
            public bool staged, pool_eligible, emissive, colliders;
            public string material_asset, shared_animation_source, build_signature;
            public string[] animations, shared_clips, signature_effects;
            public Bone[] bones;
            public Part[] parts;
            public RigAnchor[] rig_anchors;
            public TextureBinding[] texture_bindings;
        }
        [Serializable]
        private sealed class Bone
        {
            public string name, parent;
        }
        [Serializable]
        private sealed class Part
        {
            public string name, role, bone, palette_name, atlas_region;
            public float[] base_color;
        }
        [Serializable]
        private sealed class RigAnchor
        {
            public string name, bone, kind, axis_from;
            public string[] parts;
        }
        [Serializable]
        private sealed class TextureBinding
        {
            public string texture_asset, shader_property, color_space, filter_mode;
            public string wrap_mode, compression, uv_origin;
            public int width_px, height_px, uv_channel, uv_safe_inset_px;
            public string[] materials;
            public bool mipmaps;
            public string material_tint_hex, tint_source, sha256;
            public TextureRegion[] regions;
        }
        [Serializable]
        private sealed class TextureRegion
        {
            public string name, renderer;
            public int x_px, y_px, width_px, height_px;
        }
        [Serializable]
        private sealed class AnimationManifest
        {
            public string generator_version, skeleton_source, anatomy_standard;
            public float rest_pelvis_height_m;
            public int bone_count, fps, mesh_count, clip_count;
            public bool root_motion;
            public Clip[] clips;
            public string build_signature;
        }
        [Serializable]
        private sealed class Clip
        {
            public string name, archetype, authored_posture, gait;
            public float duration_seconds, loop_max_error, mattress_yaw_degrees;
            public int frame_start, frame_end, keyed_bone_count;
            public bool loop, one_shot, in_place;
            public float[] root_translation_range_m, animated_local_xz_min_m;
            public float[] animated_local_xz_max_m, animated_local_xz_size_m;
            public float[] animated_mattress_xz_min_m,
                animated_mattress_xz_max_m;
            public float[] mattress_footprint_m, mattress_used_half_extents_m;
            public float[] mattress_clearance_m;
        }
    }
    public sealed class CityArchShelterResidentModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!(assetImporter is ModelImporter importer))
            {
                return;
            }
            bool model =
                CityArchShelterResidentAssetSetup.IsOwnedModelPath(assetPath);
            bool animation = string.Equals(assetPath,
                CityArchShelterResidentAssetSetup.AnimationPath,
                StringComparison.OrdinalIgnoreCase);
            if (!model && !animation)
            {
                return;
            }
            Avatar avatar = FindAvatar();
            importer.avatarSetup = avatar != null
                ? ModelImporterAvatarSetup.CopyFromOther
                : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = animation;
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
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            if (animation)
            {
                ConfigureClips(importer);
            }
        }
        private void OnPreprocessAnimation()
        {
            if (assetImporter is ModelImporter importer &&
                string.Equals(assetPath,
                    CityArchShelterResidentAssetSetup.AnimationPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                ConfigureClips(importer);
            }
        }
        private static void ConfigureClips(ModelImporter importer)
        {
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.name = Normalize(clip.name);
                if (!CityArchShelterResidentAssetSetup.TryGetClipLoopFlag(
                        clip.name, out bool loop) || !names.Add(clip.name))
                {
                    throw new InvalidOperationException(
                        $"Undeclared/duplicate shelter clip '{clip.name}'.");
                }
                clip.loopTime = loop;
                clip.loopPose = loop;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }
        private static Avatar FindAvatar()
        {
            return AssetDatabase.LoadAllAssetsAtPath(
                    CityArchShelterResidentAssetSetup.PlayerModelPath)
                .OfType<Avatar>().FirstOrDefault();
        }
        private static string Normalize(string name)
        {
            int separator = name?.LastIndexOf('|') ?? -1;
            return separator >= 0 && separator + 1 < name.Length
                ? name.Substring(separator + 1) : name;
        }
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (NpcHumanV2AssetSetup.IsAnyPipelineBuilding)
            {
                return;
            }
            if (importedAssets.Any(
                    CityArchShelterResidentAssetSetup.IsOwnedSourcePath))
            {
                CityArchShelterResidentAssetSetup.QueueBuildWhenSourcesExist();
            }
        }
    }
    public sealed class CityArchShelterResidentTextureImporter :
        AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (assetImporter is TextureImporter importer &&
                CityArchShelterResidentAssetSetup.IsDetailAtlasPath(assetPath))
            {
                Player3DV2TextureImporter.ConfigureAtlas(importer);
            }
        }
    }
}
