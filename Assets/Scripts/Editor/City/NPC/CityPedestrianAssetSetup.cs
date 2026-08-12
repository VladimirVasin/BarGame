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
        public const string ChairCarrierModelPath =
            "Assets/Pedestrians/Models/ChairCarrierPedestrian3D.fbx";
        public const string ChairCarrierManifestPath =
            "Assets/Pedestrians/Models/ChairCarrierPedestrian3D.json";
        public const string KettleHatModelPath =
            "Assets/Pedestrians/Models/KettleHatPedestrian3D.fbx";
        public const string KettleHatManifestPath =
            "Assets/Pedestrians/Models/KettleHatPedestrian3D.json";
        public const string LongArmModelPath =
            "Assets/Pedestrians/Models/LongArmPedestrian3D.fbx";
        public const string LongArmManifestPath =
            "Assets/Pedestrians/Models/LongArmPedestrian3D.json";
        public const string HelmetLampModelPath =
            "Assets/Pedestrians/Models/HelmetLampPedestrian3D.fbx";
        public const string HelmetLampManifestPath =
            "Assets/Pedestrians/Models/HelmetLampPedestrian3D.json";
        public const string PlayerModelPath =
            "Assets/Player3D/Models/PlayerCharacter3D.fbx";
        public const string AnimationPath =
            "Assets/Pedestrians/Animations/CityPedestrianLocomotion.fbx";
        public const string AnimationManifestPath =
            "Assets/Pedestrians/Animations/CityPedestrianLocomotion.json";
        public const string PlayerAnimationPath =
            "Assets/Player3D/Animations/PlayerCharacter3DAnimations.fbx";
        public const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        public const string PrefabPath =
            "Assets/Resources/Pedestrians/CityPedestrian3D.prefab";
        public const string ChairCarrierPrefabPath =
            "Assets/Resources/Pedestrians/ChairCarrierPedestrian3D.prefab";
        public const string KettleHatPrefabPath =
            "Assets/Resources/Pedestrians/KettleHatPedestrian3D.prefab";
        public const string LongArmPrefabPath =
            "Assets/Resources/Pedestrians/LongArmPedestrian3D.prefab";
        public const string HelmetLampPrefabPath =
            "Assets/Resources/Pedestrians/HelmetLampPedestrian3D.prefab";

        // The one worn lamp the pedestrian contract allows. It stays
        // shadowless and short-range so a single moving Spot cannot disturb
        // the City's bounded night-fixture budget.
        private const float HeadLampRange = 7.5f;
        private const float HeadLampIntensity = 3.6f;
        private const float HeadLampSpotAngle = 58f;
        private const float HeadLampInnerSpotAngle = 26f;
        private static readonly Vector3 HeadLampLocalPosition =
            new Vector3(0f, 0.166f, -0.234f);

        private const string ExpectedPose = "apose";
        private const float ExpectedHeight = 1.75f;
        private const int ExpectedBoneCount = 31;
        private const int ExpectedAnimationFps = 24;
        private const float TransformPositionTolerance = 0.0001f;
        private const float TransformAngleTolerance = 0.02f;

        private static readonly PedestrianDescriptor[] Descriptors =
        {
            new PedestrianDescriptor(
                "Lampshade Walker",
                "CityPedestrian3D",
                "lampshade_walker_v1",
                ModelPath,
                ManifestPath,
                PrefabPath,
                "LampshadeIdle",
                "LampshadeWalk",
                2f,
                1.25f,
                800,
                1400),
            new PedestrianDescriptor(
                "Chair Carrier",
                "ChairCarrierPedestrian3D",
                "chair_carrier_v1",
                ChairCarrierModelPath,
                ChairCarrierManifestPath,
                ChairCarrierPrefabPath,
                "ChairCarrierIdle",
                "ChairCarrierWalk",
                1.5f,
                1f,
                800,
                1600),
            new PedestrianDescriptor(
                "Kettle Hat Walker",
                "KettleHatPedestrian3D",
                "kettle_hat_walker_v1",
                KettleHatModelPath,
                KettleHatManifestPath,
                KettleHatPrefabPath,
                "KettleHatIdle",
                "KettleHatWalk",
                1.75f,
                0.75f,
                800,
                1600),
            new PedestrianDescriptor(
                "Long-Arm Walker",
                "LongArmPedestrian3D",
                "long_arm_walker_v1",
                LongArmModelPath,
                LongArmManifestPath,
                LongArmPrefabPath,
                "LongArmIdle",
                "LongArmWalk",
                2.5f,
                1.5f,
                800,
                1300),
            new PedestrianDescriptor(
                "Helmet Lamp Hopper",
                "HelmetLampPedestrian3D",
                "helmet_lamp_hopper_v1",
                HelmetLampModelPath,
                HelmetLampManifestPath,
                HelmetLampPrefabPath,
                "HelmetLampIdle",
                "HelmetLampHop",
                2f,
                1f,
                800,
                1700,
                carriesHeadLamp: true,
                preservesAirborneMotion: true)
        };

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
                "City pedestrian archetype prefabs rebuilt.");
        }

        [MenuItem("Bar Promenade/City Pedestrian 3D/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log(
                "City pedestrian models, custom clips and prefab contracts " +
                "are valid.");
        }

        /// <summary>
        /// True for clips whose archetype deliberately leaves the pavement.
        /// Their vertical travel is authored on the pelvis, which this rig's
        /// Avatar treats as the motion node, so locking root height would
        /// silently strip the hop during import.
        /// </summary>
        public static bool IsAirborneClip(string normalizedClipName)
        {
            if (string.IsNullOrEmpty(normalizedClipName))
            {
                return false;
            }

            for (int index = 0; index < Descriptors.Length; index++)
            {
                PedestrianDescriptor descriptor = Descriptors[index];
                if (!descriptor.PreservesAirborneMotion)
                {
                    continue;
                }

                if (string.Equals(
                        normalizedClipName,
                        descriptor.IdleClipName,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        normalizedClipName,
                        descriptor.WalkClipName,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateDeclaredHeadLamp(
            GameObject prefab,
            CityPedestrianAssetRegistry registry,
            PedestrianDescriptor descriptor)
        {
            // Pedestrian presentations stay light-free unless the descriptor
            // declares exactly one worn lamp. The count is checked rather than
            // merely forbidden, so an accidental extra Light still fails.
            Light[] lights = prefab.GetComponentsInChildren<Light>(true);
            if (lights.Length != descriptor.ExpectedLightCount)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DesignId}' declares " +
                    $"{descriptor.ExpectedLightCount} worn light(s) but the " +
                    $"prefab carries {lights.Length}.");
            }

            if (registry.PreservesAirborneMotion !=
                descriptor.PreservesAirborneMotion)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DesignId}' airborne-motion flag does not " +
                    "match its descriptor.");
            }

            if (!descriptor.CarriesHeadLamp)
            {
                if (registry.HeadLamp != null)
                {
                    throw new InvalidOperationException(
                        $"'{descriptor.DesignId}' must not register a head " +
                        "lamp.");
                }

                return;
            }

            Light lamp = registry.HeadLamp;
            if (lamp == null || lamp != lights[0])
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DesignId}' does not register its worn " +
                    "lamp on the asset registry.");
            }

            if (lamp.type != LightType.Spot ||
                lamp.shadows != LightShadows.None ||
                lamp.range > HeadLampRange + 0.001f ||
                lamp.intensity > HeadLampIntensity + 0.001f)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DesignId}' head lamp must stay a " +
                    "shadowless Spot within its bounded range and intensity.");
            }

            if (lamp.transform.parent != registry.HeadAnchor)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DesignId}' head lamp must hang off the " +
                    "animated head bone.");
            }
        }

        private static Light CreateHeadLamp(Transform head)
        {
            // Parented to the head bone so the beam follows the real animated
            // skull rather than a static socket. It is deliberately always on:
            // a miner's lamp is lit because its owner switched it on, not
            // because the city clock reached dusk.
            GameObject lampObject = new GameObject("Head Lamp");
            lampObject.transform.SetParent(head, false);
            lampObject.transform.localPosition = HeadLampLocalPosition;
            lampObject.transform.localRotation =
                Quaternion.LookRotation(Vector3.back, Vector3.up);
            lampObject.transform.localScale = Vector3.one;

            Light lamp = lampObject.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.shadows = LightShadows.None;
            lamp.range = HeadLampRange;
            lamp.intensity = HeadLampIntensity;
            lamp.spotAngle = HeadLampSpotAngle;
            lamp.innerSpotAngle = HeadLampInnerSpotAngle;
            lamp.color = new Color(1f, 0.925f, 0.78f);
            lamp.cullingMask = CityPedestrianCollision.NonPedestrianMask;
            lamp.lightmapBakeType = LightmapBakeType.Realtime;
            return lamp;
        }

        public static bool SourcesExist()
        {
            if (!File.Exists(PlayerModelPath) ||
                !File.Exists(AnimationPath) ||
                !File.Exists(AnimationManifestPath) ||
                !File.Exists(SharedMaterialPath))
            {
                return false;
            }

            for (int index = 0; index < Descriptors.Length; index++)
            {
                if (!File.Exists(Descriptors[index].ModelPath) ||
                    !File.Exists(Descriptors[index].ManifestPath))
                {
                    return false;
                }
            }

            return true;
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
                    "City pedestrian build requires both model FBX/manifest " +
                    "pairs, the custom locomotion FBX/manifest, production " +
                    "Player model and shared Player3DLit material.");
            }

            isBuilding = true;
            try
            {
                for (int index = 0; index < Descriptors.Length; index++)
                {
                    EnsureFolderForAsset(Descriptors[index].PrefabPath);
                }

                // Import the Avatar dependency first so a clean Library and
                // later Player-rig changes rebuild every source against
                // the canonical external Generic Avatar.
                AssetDatabase.ImportAsset(
                    PlayerModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                for (int index = 0; index < Descriptors.Length; index++)
                {
                    AssetDatabase.ImportAsset(
                        Descriptors[index].ModelPath,
                        ImportAssetOptions.ForceUpdate |
                        ImportAssetOptions.ForceSynchronousImport);
                    AssetDatabase.ImportAsset(
                        Descriptors[index].ManifestPath,
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

                CityPedestrianAnimationManifest animationManifest =
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
                    PedestrianDescriptor descriptor = Descriptors[index];
                    CityPedestrianManifest manifest =
                        LoadAndValidateManifest(descriptor);
                    GameObject modelAsset =
                        AssetDatabase.LoadAssetAtPath<GameObject>(
                            descriptor.ModelPath);
                    if (modelAsset == null)
                    {
                        throw new InvalidOperationException(
                            $"Unity did not import a model from " +
                            $"'{descriptor.ModelPath}'.");
                    }

                    AnimationClip idle = LoadLocomotionClip(
                        descriptor.IdleClipName,
                        descriptor.IdleDuration,
                        animationManifest);
                    AnimationClip walk = LoadLocomotionClip(
                        descriptor.WalkClipName,
                        descriptor.WalkDuration,
                        animationManifest);
                    BuildPrefab(
                        descriptor,
                        modelAsset,
                        sharedMaterial,
                        idle,
                        walk,
                        manifest);
                }

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
            CityPedestrianAnimationManifest animationManifest =
                LoadAndValidateAnimationManifest();
            for (int index = 0; index < Descriptors.Length; index++)
            {
                ValidateDescriptor(Descriptors[index], animationManifest);
            }
        }

        private static void ValidateDescriptor(
            PedestrianDescriptor descriptor,
            CityPedestrianAnimationManifest animationManifest)
        {
            CityPedestrianManifest manifest =
                LoadAndValidateManifest(descriptor);
            ValidateImportedModel(descriptor, manifest);

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"City pedestrian prefab is missing at " +
                    $"'{descriptor.PrefabPath}'.");
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

            ValidateLocomotionClip(
                registry.IdleClip,
                descriptor.IdleClipName,
                descriptor.IdleDuration,
                animationManifest);
            ValidateLocomotionClip(
                registry.WalkClip,
                descriptor.WalkClipName,
                descriptor.WalkDuration,
                animationManifest);
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

            if (Mathf.Abs(registry.LocalBounds.size.y - descriptor.Height) >
                    0.035f ||
                Mathf.Abs(registry.LocalBounds.min.y) > 0.025f)
            {
                throw new InvalidOperationException(
                    "City pedestrian prefab bounds lost canonical height or " +
                    "grounding.");
            }

            ValidateDeclaredHeadLamp(prefab, registry, descriptor);

            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Collider2D>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody2D>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Camera>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AudioSource>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Atmospheric pedestrian prefabs must stay passive: no " +
                    "physics bodies, colliders, lights, cameras or audio.");
            }

            MonoBehaviour[] behaviours =
                prefab.GetComponentsInChildren<MonoBehaviour>(true);
            if (behaviours.Any(behaviour =>
                    behaviour != null &&
                    !(behaviour is CityPedestrianAssetRegistry) &&
                    behaviour is IInteractable))
            {
                throw new InvalidOperationException(
                    "Atmospheric pedestrian prefabs must not be interactive.");
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

            try
            {
                LoadAndValidateAnimationManifest();
                for (int index = 0; index < Descriptors.Length; index++)
                {
                    PedestrianDescriptor descriptor = Descriptors[index];
                    CityPedestrianManifest manifest =
                        LoadAndValidateManifest(descriptor);
                    GameObject prefab =
                        AssetDatabase.LoadAssetAtPath<GameObject>(
                            descriptor.PrefabPath);
                    CityPedestrianAssetRegistry registry = prefab != null
                        ? prefab.GetComponent<CityPedestrianAssetRegistry>()
                        : null;
                    if (registry == null ||
                        !string.Equals(
                            registry.BuildSignature,
                            manifest.build_signature,
                            StringComparison.Ordinal) ||
                        registry.IdleClip == null ||
                        registry.WalkClip == null ||
                        !string.Equals(
                            NormalizeClipName(registry.IdleClip.name),
                            descriptor.IdleClipName,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            NormalizeClipName(registry.WalkClip.name),
                            descriptor.WalkClipName,
                            StringComparison.Ordinal))
                    {
                        QueueBuildWhenSourcesExist();
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Could not validate City pedestrian source manifest: " +
                    $"{exception}");
                return;
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

        private static CityPedestrianManifest LoadAndValidateManifest(
            PedestrianDescriptor descriptor)
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    descriptor.ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import manifest " +
                    $"'{descriptor.ManifestPath}'.");
            }

            CityPedestrianManifest manifest =
                JsonUtility.FromJson<CityPedestrianManifest>(source.text);
            if (manifest == null ||
                manifest.parts == null ||
                manifest.bones == null ||
                manifest.shared_clips == null ||
                manifest.triangle_budget == null ||
                manifest.triangle_budget.Length != 2)
            {
                throw new InvalidOperationException(
                    "City pedestrian manifest is malformed.");
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
                    "City pedestrian design/pose/axis contract differs from " +
                    $"the approved {descriptor.DisplayName}.");
            }

            if (Mathf.Abs(manifest.height_m - descriptor.Height) > 0.0001f ||
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
                    AnimationPath,
                    StringComparison.Ordinal) ||
                !manifest.shared_clips.SequenceEqual(
                    new[]
                    {
                        descriptor.IdleClipName,
                        descriptor.WalkClipName
                    }))
            {
                throw new InvalidOperationException(
                    "City pedestrian must be non-emissive, collider-free, " +
                    "animation-free and reuse Player3DLit plus its custom " +
                    "idle/walk clips.");
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

        private static CityPedestrianAnimationManifest
            LoadAndValidateAnimationManifest()
        {
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(
                AnimationManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import locomotion manifest " +
                    $"'{AnimationManifestPath}'.");
            }

            CityPedestrianAnimationManifest manifest = JsonUtility.FromJson<
                CityPedestrianAnimationManifest>(source.text);
            if (manifest == null ||
                manifest.clips == null ||
                manifest.fps != ExpectedAnimationFps ||
                manifest.bone_count != ExpectedBoneCount ||
                manifest.mesh_count != 0 ||
                manifest.clip_count != Descriptors.Length * 2 ||
                manifest.root_motion ||
                string.IsNullOrWhiteSpace(manifest.skeleton_source) ||
                string.IsNullOrWhiteSpace(manifest.generator_version) ||
                string.IsNullOrWhiteSpace(manifest.build_signature) ||
                manifest.build_signature.Length != 64)
            {
                throw new InvalidOperationException(
                    "City pedestrian locomotion manifest has an invalid " +
                    "skeleton, root-motion or source metadata contract.");
            }

            Dictionary<string, PedestrianDescriptor> clipOwners =
                new Dictionary<string, PedestrianDescriptor>(
                    StringComparer.Ordinal);
            for (int index = 0; index < Descriptors.Length; index++)
            {
                PedestrianDescriptor descriptor = Descriptors[index];
                clipOwners.Add(descriptor.IdleClipName, descriptor);
                clipOwners.Add(descriptor.WalkClipName, descriptor);
            }

            HashSet<string> names =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.clips.Length; index++)
            {
                CityPedestrianAnimationManifestClip clip =
                    manifest.clips[index];
                if (clip == null ||
                    string.IsNullOrWhiteSpace(clip.name) ||
                    !names.Add(clip.name) ||
                    !clipOwners.TryGetValue(
                        clip.name,
                        out PedestrianDescriptor owner) ||
                    !string.Equals(
                        clip.archetype,
                        owner.DesignId,
                        StringComparison.Ordinal) ||
                    !clip.loop ||
                    !clip.in_place ||
                    clip.keyed_bone_count != ExpectedBoneCount ||
                    string.IsNullOrWhiteSpace(clip.authored_posture) ||
                    string.IsNullOrWhiteSpace(clip.gait) ||
                    Mathf.Abs(clip.loop_max_error) > 0.0001f ||
                    clip.frame_start != 0 ||
                    clip.frame_end != Mathf.RoundToInt(
                        clip.duration_seconds * ExpectedAnimationFps) ||
                    clip.root_translation_range_m == null ||
                    clip.root_translation_range_m.Length != 3 ||
                    clip.root_translation_range_m.Any(component =>
                        Mathf.Abs(component) > 0.0001f))
                {
                    throw new InvalidOperationException(
                        $"Locomotion manifest clip {index} violates the " +
                        "approved looping, in-place Generic contract.");
                }

                float expectedDuration = string.Equals(
                    clip.name,
                    owner.IdleClipName,
                    StringComparison.Ordinal)
                    ? owner.IdleDuration
                    : owner.WalkDuration;
                if (Mathf.Abs(
                        clip.duration_seconds - expectedDuration) > 0.0001f)
                {
                    throw new InvalidOperationException(
                        $"Locomotion manifest clip '{clip.name}' has an " +
                        "unexpected duration.");
                }
            }

            if (names.Count != clipOwners.Count ||
                clipOwners.Keys.Any(name => !names.Contains(name)))
            {
                throw new InvalidOperationException(
                    "Locomotion manifest must contain exactly the four " +
                    "approved archetype clips.");
            }

            ModelImporter importer =
                AssetImporter.GetAtPath(AnimationPath) as ModelImporter;
            Avatar playerAvatar = FindModelAvatar();
            if (importer == null ||
                !importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.Generic ||
                importer.avatarSetup !=
                    ModelImporterAvatarSetup.CopyFromOther ||
                playerAvatar == null ||
                importer.sourceAvatar != playerAvatar)
            {
                throw new InvalidOperationException(
                    "Locomotion FBX must import animation as Generic and " +
                    "copy the production Player Avatar.");
            }

            AnimationClip[] importedClips = AssetDatabase
                .LoadAllAssetsAtPath(AnimationPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith(
                    "__preview__",
                    StringComparison.Ordinal))
                .ToArray();
            if (importedClips.Length != clipOwners.Count)
            {
                throw new InvalidOperationException(
                    $"Unity imported {importedClips.Length} pedestrian " +
                    $"locomotion clips; expected {clipOwners.Count}.");
            }

            GameObject animationAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(AnimationPath);
            if (animationAsset == null ||
                animationAsset.GetComponentsInChildren<Renderer>(true).Length !=
                    0)
            {
                throw new InvalidOperationException(
                    "Pedestrian locomotion FBX must remain animation-only " +
                    "and contain no renderable meshes.");
            }

            Dictionary<string, Transform> animationTransforms =
                IndexUniqueTransforms(animationAsset, "locomotion");
            Transform animationBoneRoot = RequireTransform(
                animationTransforms,
                "root",
                "locomotion");
            if (animationBoneRoot
                    .GetComponentsInChildren<Transform>(true).Length !=
                ExpectedBoneCount)
            {
                throw new InvalidOperationException(
                    "Pedestrian locomotion FBX has added or missing Generic " +
                    "bones.");
            }

            return manifest;
        }

        private static void ValidateImportedModel(
            PedestrianDescriptor descriptor,
            CityPedestrianManifest manifest)
        {
            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.ModelPath);
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
                AssetDatabase.LoadAllAssetsAtPath(descriptor.ModelPath);
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
                AssetImporter.GetAtPath(descriptor.ModelPath) as ModelImporter;
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
            PedestrianDescriptor descriptor,
            GameObject modelAsset,
            Material sharedMaterial,
            AnimationClip idle,
            AnimationClip walk,
            CityPedestrianManifest manifest)
        {
            GameObject prefabRoot = new GameObject(descriptor.PrefabRootName);
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

                Light headLamp = descriptor.CarriesHeadLamp
                    ? CreateHeadLamp(head)
                    : null;
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
                    manifest.build_signature,
                    headLamp,
                    descriptor.PreservesAirborneMotion);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    prefabRoot,
                    descriptor.PrefabPath,
                    out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save City pedestrian prefab at " +
                        $"'{descriptor.PrefabPath}'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        private static AnimationClip LoadLocomotionClip(
            string clipName,
            float expectedDuration,
            CityPedestrianAnimationManifest animationManifest)
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
                        clipName,
                        StringComparison.Ordinal));
            ValidateLocomotionClip(
                clip,
                clipName,
                expectedDuration,
                animationManifest);
            return clip;
        }

        private static void ValidateLocomotionClip(
            AnimationClip clip,
            string expectedName,
            float expectedDuration,
            CityPedestrianAnimationManifest animationManifest)
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
                    AnimationPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Pedestrian '{expectedName}' must directly reference " +
                    $"the looping custom locomotion clip at " +
                    $"'{AnimationPath}'.");
            }

            CityPedestrianAnimationManifestClip source =
                animationManifest.clips.FirstOrDefault(candidate =>
                    candidate != null &&
                    string.Equals(
                        candidate.name,
                        expectedName,
                        StringComparison.Ordinal));
            if (source == null ||
                Mathf.Abs(source.duration_seconds - expectedDuration) >
                    0.0001f)
            {
                throw new InvalidOperationException(
                    $"Locomotion manifest does not describe '{expectedName}' " +
                    "with the approved duration.");
            }
        }

        private static Color BuildPaletteVariant(
            string paletteName,
            Color baseColor,
            int variant)
        {
            if (!IsVariantPalette(paletteName))
            {
                return baseColor;
            }

            Vector3 multiplier;
            if (paletteName.StartsWith(
                    "work_",
                    StringComparison.Ordinal))
            {
                // Chair Carrier variants: tobacco/base, bottle green,
                // faded burgundy and cold grey-blue.
                multiplier = variant == 1
                    ? new Vector3(0.42f, 0.72f, 0.50f)
                    : variant == 2
                        ? new Vector3(0.92f, 0.47f, 0.52f)
                        : new Vector3(0.62f, 0.72f, 0.92f);
            }
            else if (variant == 1)
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

        private static bool IsVariantPalette(string paletteName)
        {
            return !string.Equals(
                       paletteName,
                       "void",
                       StringComparison.Ordinal) &&
                   !string.Equals(
                       paletteName,
                       "sole",
                       StringComparison.Ordinal) &&
                   !string.Equals(
                       paletteName,
                       "amber",
                       StringComparison.Ordinal) &&
                   !paletteName.StartsWith(
                       "chair_",
                       StringComparison.Ordinal);
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
            public int[] triangle_budget;
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

        private sealed class PedestrianDescriptor
        {
            public PedestrianDescriptor(
                string displayName,
                string prefabRootName,
                string designId,
                string modelPath,
                string manifestPath,
                string prefabPath,
                string idleClipName,
                string walkClipName,
                float idleDuration,
                float walkDuration,
                int minimumTriangleCount,
                int maximumTriangleCount,
                bool carriesHeadLamp = false,
                bool preservesAirborneMotion = false)
            {
                CarriesHeadLamp = carriesHeadLamp;
                PreservesAirborneMotion = preservesAirborneMotion;
                DisplayName = displayName;
                PrefabRootName = prefabRootName;
                DesignId = designId;
                ModelPath = modelPath;
                ManifestPath = manifestPath;
                PrefabPath = prefabPath;
                IdleClipName = idleClipName;
                WalkClipName = walkClipName;
                IdleDuration = idleDuration;
                WalkDuration = walkDuration;
                MinimumTriangleCount = minimumTriangleCount;
                MaximumTriangleCount = maximumTriangleCount;
            }

            public string DisplayName { get; }
            public string PrefabRootName { get; }
            public string DesignId { get; }
            public string ModelPath { get; }
            public string ManifestPath { get; }
            public string PrefabPath { get; }
            public string IdleClipName { get; }
            public string WalkClipName { get; }
            public float IdleDuration { get; }
            public float WalkDuration { get; }
            public int MinimumTriangleCount { get; }
            public int MaximumTriangleCount { get; }

            /// <summary>
            /// Declares the one shadowless Spot this design wears. Pedestrian
            /// presentations are otherwise light-free; a design that wants a
            /// working lamp has to say so here rather than smuggling a Light
            /// into the prefab.
            /// </summary>
            public bool CarriesHeadLamp { get; }

            /// <summary>
            /// Declares that this design's clips leave the pavement, so the
            /// runtime must not pin its lowest sole every frame.
            /// </summary>
            public bool PreservesAirborneMotion { get; }

            public int ExpectedLightCount => CarriesHeadLamp ? 1 : 0;
            public float Height => ExpectedHeight;
        }

        [Serializable]
        private sealed class CityPedestrianAnimationManifest
        {
            public string generator_version;
            public string skeleton_source;
            public int bone_count;
            public int fps;
            public bool root_motion;
            public int mesh_count;
            public int clip_count;
            public string build_signature;
            public CityPedestrianAnimationManifestClip[] clips;
        }

        [Serializable]
        private sealed class CityPedestrianAnimationManifestClip
        {
            public string name;
            public string archetype;
            public float duration_seconds;
            public int frame_start;
            public int frame_end;
            public bool loop;
            public bool in_place;
            public string authored_posture;
            public string gait;
            public int keyed_bone_count;
            public float loop_max_error;
            public float[] root_translation_range_m;
        }
    }
}
