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
    public static class CityBusDriverAssetSetup
    {
        public const string ModelPath =
            "Assets/Vehicles/Drivers/Models/CityBusDriver3D.fbx";
        public const string ManifestPath =
            "Assets/Vehicles/Drivers/Models/CityBusDriver3D.json";
        public const string PlayerModelPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        public const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        public const string PrefabPath =
            "Assets/Resources/Vehicles/CityBusDriver3D.prefab";

        private const string ExpectedDesignId = "long_eyes_driver_v1";
        private const string ExpectedPose = "apose";
        private const string ExpectedHeadDesign =
            "ordinary_low_poly_human";
        private const string ExpectedEyeDesign =
            "two_long_horizontal_eyes_with_visible_pupils";
        private const float ExpectedHeight = 1.75f;
        private const int MinimumTriangleCount = 900;
        private const int MaximumTriangleCount = 1800;
        private const float TransformPositionTolerance = 0.0001f;
        private const float TransformAngleTolerance = 0.02f;

        private static readonly string[] RequiredBoneNames =
        {
            "pelvis",
            "spine",
            "chest",
            "neck",
            "head",
            "face.eye.L",
            "face.eye.R",
            "upper_arm.L",
            "forearm.L",
            "hand.L",
            "SOCKET_Grip.L",
            "upper_arm.R",
            "forearm.R",
            "hand.R",
            "SOCKET_Grip.R",
            "thigh.L",
            "shin.L",
            "foot.L",
            "thigh.R",
            "shin.R",
            "foot.R"
        };

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static CityBusDriverAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/City Bus Driver 3D/Build Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log($"City bus driver prefab rebuilt at '{PrefabPath}'.");
        }

        [MenuItem("Bar Promenade/City Bus Driver 3D/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log(
                "City bus driver imported model and passive prefab contract " +
                "are valid.");
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
                    "City bus driver build requires its FBX/manifest, the " +
                    "production Hero/NPC V2 model and shared Player3DLit " +
                    "material.");
            }

            isBuilding = true;
            try
            {
                EnsureFolderForAsset(PrefabPath);
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

                CityBusDriverManifest manifest = LoadAndValidateManifest();
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

                BuildPrefab(modelAsset, sharedMaterial, manifest);
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
            CityBusDriverManifest manifest = LoadAndValidateManifest();
            ValidateImportedModel(manifest);

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"City bus driver prefab is missing at '{PrefabPath}'.");
            }

            CityBusDriverAssetRegistry registry =
                prefab.GetComponent<CityBusDriverAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "City bus driver prefab has no asset registry.");
            }

            Avatar playerAvatar = FindModelAvatar();
            if (registry.Animator == null ||
                registry.Animator.applyRootMotion ||
                registry.Animator.runtimeAnimatorController != null ||
                registry.Animator.avatar == null ||
                registry.Animator.avatar != playerAvatar ||
                registry.Animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            {
                throw new InvalidOperationException(
                    "City bus driver Animator must be controller-free, use " +
                    "the Hero/NPC V2 Generic Avatar, always update and " +
                    "disable root motion.");
            }

            ValidateRegistryBindings(registry);
            if (registry.Renderers.Count != manifest.mesh_count ||
                registry.RendererBindings.Count != manifest.mesh_count)
            {
                throw new InvalidOperationException(
                    "City bus driver renderer counts differ from the " +
                    "deterministic manifest.");
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
                    "City bus driver registry source metadata is stale.");
            }

            if (Mathf.Abs(registry.LocalBounds.size.y - ExpectedHeight) >
                    0.035f ||
                Mathf.Abs(registry.LocalBounds.min.y) > 0.025f)
            {
                throw new InvalidOperationException(
                    "City bus driver prefab bounds lost canonical height or " +
                    "grounding.");
            }

            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "The passive driver prefab must contain no Collider, " +
                    "Light or Rigidbody component.");
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
                        "Every driver renderer must reference the one shared " +
                        "Player3DLit material.");
                }
            }

            ValidateEyeBindings(registry.RendererBindings);
        }

        private static void ValidateDependencyStamp()
        {
            if (!SourcesExist())
            {
                return;
            }

            CityBusDriverManifest manifest;
            try
            {
                manifest = LoadAndValidateManifest();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Could not validate City bus driver source manifest: " +
                    $"{exception}");
                return;
            }

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            CityBusDriverAssetRegistry registry = prefab != null
                ? prefab.GetComponent<CityBusDriverAssetRegistry>()
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
                    $"Could not build City bus driver prefab: {exception}");
            }
        }

        private static CityBusDriverManifest LoadAndValidateManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import manifest '{ManifestPath}'.");
            }

            CityBusDriverManifest manifest =
                JsonUtility.FromJson<CityBusDriverManifest>(source.text);
            if (manifest == null ||
                manifest.parts == null ||
                manifest.bones == null ||
                manifest.animations == null)
            {
                throw new InvalidOperationException(
                    "City bus driver manifest is malformed.");
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
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.head_design,
                    ExpectedHeadDesign,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.eye_design,
                    ExpectedEyeDesign,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "City bus driver design, head, eyes, pose or axis " +
                    "contract differs from the approved source.");
            }

            if (Mathf.Abs(manifest.height_m - ExpectedHeight) > 0.0001f ||
                manifest.mesh_count != manifest.parts.Length ||
                manifest.mesh_count < 24 ||
                manifest.mesh_count > 48 ||
                manifest.bones.Length != 31 ||
                manifest.triangle_count < MinimumTriangleCount ||
                manifest.triangle_count > MaximumTriangleCount)
            {
                throw new InvalidOperationException(
                    "City bus driver manifest height, skeleton, mesh or " +
                    "triangle budget is invalid.");
            }

            if (manifest.emissive ||
                manifest.colliders ||
                manifest.lights ||
                manifest.rigidbodies ||
                manifest.animation_count != 0 ||
                manifest.animations.Length != 0 ||
                !string.Equals(
                    manifest.material_asset,
                    SharedMaterialPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "City bus driver must be non-emissive, collider/light/" +
                    "Rigidbody-free, animation-free and reuse Player3DLit.");
            }

            if (string.IsNullOrWhiteSpace(manifest.generator_version) ||
                string.IsNullOrWhiteSpace(manifest.build_signature) ||
                manifest.build_signature.Length != 64)
            {
                throw new InvalidOperationException(
                    "City bus driver manifest lacks deterministic source " +
                    "metadata.");
            }

            HashSet<string> partNames =
                new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> boneNames =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.bones.Length; index++)
            {
                CityBusDriverManifestBone bone = manifest.bones[index];
                if (bone == null ||
                    string.IsNullOrEmpty(bone.name) ||
                    !boneNames.Add(bone.name))
                {
                    throw new InvalidOperationException(
                        "City bus driver manifest contains a missing or " +
                        "duplicate bone.");
                }
            }

            for (int index = 0; index < RequiredBoneNames.Length; index++)
            {
                if (!boneNames.Contains(RequiredBoneNames[index]))
                {
                    throw new InvalidOperationException(
                        $"City bus driver skeleton is missing canonical " +
                        $"bone '{RequiredBoneNames[index]}'.");
                }
            }

            for (int index = 0; index < manifest.parts.Length; index++)
            {
                CityBusDriverManifestPart part = manifest.parts[index];
                if (part == null ||
                    string.IsNullOrEmpty(part.name) ||
                    string.IsNullOrEmpty(part.role) ||
                    string.IsNullOrEmpty(part.bone) ||
                    string.IsNullOrEmpty(part.palette_name) ||
                    part.base_color == null ||
                    part.base_color.Length != 4 ||
                    !partNames.Add(part.name) ||
                    !boneNames.Contains(part.bone))
                {
                    throw new InvalidOperationException(
                        "City bus driver manifest contains an invalid part " +
                        "binding.");
                }
            }

            ValidateManifestDesignParts(manifest.parts);
            return manifest;
        }

        private static void ValidateManifestDesignParts(
            IReadOnlyList<CityBusDriverManifestPart> parts)
        {
            Dictionary<string, CityBusDriverManifestPart> byName =
                parts.ToDictionary(part => part.name, StringComparer.Ordinal);
            RequirePart(byName, "GEO_Head", "human_head", "head");
            RequirePart(
                byName,
                "FACE_EyeWhite.L",
                "long_horizontal_eye",
                "head");
            RequirePart(
                byName,
                "FACE_EyeWhite.R",
                "long_horizontal_eye",
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
                "CLO_BroadCuff.L",
                "mismatched_cuff",
                "forearm.L");
            RequirePart(
                byName,
                "CLO_TightCuff.R",
                "mismatched_cuff",
                "forearm.R");

            if (byName.Keys.Any(name =>
                    name.IndexOf(
                        "lampshade",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf(
                        "hood",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf(
                        "facevoid",
                        StringComparison.OrdinalIgnoreCase) >= 0))
            {
                throw new InvalidOperationException(
                    "The driver source may not replace or conceal the human " +
                    "head with an object silhouette.");
            }
        }

        private static void RequirePart(
            IReadOnlyDictionary<string, CityBusDriverManifestPart> parts,
            string name,
            string role,
            string bone)
        {
            if (!parts.TryGetValue(
                    name,
                    out CityBusDriverManifestPart part) ||
                !string.Equals(part.role, role, StringComparison.Ordinal) ||
                !string.Equals(part.bone, bone, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"City bus driver manifest lost required part '{name}' " +
                    $"with role '{role}' on '{bone}'.");
            }
        }

        private static void ValidateImportedModel(
            CityBusDriverManifest manifest)
        {
            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            GameObject playerModel =
                AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
            if (model == null || playerModel == null)
            {
                throw new InvalidOperationException(
                    "Driver or Hero/NPC V2 source model failed to import.");
            }

            Dictionary<string, Transform> driverTransforms =
                IndexUniqueTransforms(model, "driver");
            Dictionary<string, Transform> playerTransforms =
                IndexUniqueTransforms(playerModel, "player");
            for (int index = 0; index < manifest.bones.Length; index++)
            {
                CityBusDriverManifestBone source = manifest.bones[index];
                Transform driver = RequireTransform(
                    driverTransforms,
                    source.name,
                    "driver");
                Transform player = RequireTransform(
                    playerTransforms,
                    source.name,
                    "player");
                string expectedParent = string.IsNullOrEmpty(source.parent)
                    ? "RIG_Player"
                    : source.parent;
                if (driver.parent == null ||
                    !string.Equals(
                        driver.parent.name,
                        expectedParent,
                        StringComparison.Ordinal) ||
                    player.parent == null ||
                    !string.Equals(
                        player.parent.name,
                        expectedParent,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Bone '{source.name}' lost the exact Hero/NPC V2 " +
                        "parent " +
                        $"'{expectedParent}'.");
                }

                if (Vector3.Distance(
                        driver.localPosition,
                        player.localPosition) > TransformPositionTolerance ||
                    Quaternion.Angle(
                        driver.localRotation,
                        player.localRotation) > TransformAngleTolerance ||
                    Vector3.Distance(
                        driver.localScale,
                        player.localScale) > TransformPositionTolerance)
                {
                    throw new InvalidOperationException(
                        $"Bone '{source.name}' rest transform differs from " +
                        "PlayerCharacter3DV2.");
                }
            }

            Transform boneRoot = RequireTransform(
                driverTransforms,
                "root",
                "driver");
            if (boneRoot.GetComponentsInChildren<Transform>(true).Length !=
                manifest.bones.Length)
            {
                throw new InvalidOperationException(
                    "Driver armature has added or missing Generic bones.");
            }

            Renderer[] renderers =
                model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != manifest.mesh_count)
            {
                throw new InvalidOperationException(
                    $"Unity imported {renderers.Length} driver meshes; " +
                    $"manifest declares {manifest.mesh_count}.");
            }

            int triangles = CountTriangles(renderers);
            if (triangles != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    $"Unity imported {triangles} driver triangles; manifest " +
                    $"declares {manifest.triangle_count}.");
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
                    "Driver FBX unexpectedly imported an animation clip.");
            }

            Avatar playerAvatar = FindModelAvatar();
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (playerAvatar == null ||
                !playerAvatar.isValid ||
                importer == null ||
                importer.avatarSetup !=
                    ModelImporterAvatarSetup.CopyFromOther ||
                importer.sourceAvatar != playerAvatar)
            {
                throw new InvalidOperationException(
                    "Driver FBX must copy the valid production Hero/NPC V2 " +
                    "Generic Avatar.");
            }
        }

        private static void BuildPrefab(
            GameObject modelAsset,
            Material sharedMaterial,
            CityBusDriverManifest manifest)
        {
            GameObject prefabRoot = new GameObject("CityBusDriver3D");
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate imported bus driver model.");
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
                    IndexUniqueTransforms(model, "driver prefab");
                if (renderersByName.Count != manifest.mesh_count)
                {
                    throw new InvalidOperationException(
                        "Imported driver renderer count differs from the " +
                        "manifest.");
                }

                List<Renderer> rendererList =
                    new List<Renderer>(manifest.parts.Length);
                List<CityBusDriverRendererBinding> bindings =
                    new List<CityBusDriverRendererBinding>(
                        manifest.parts.Length);
                for (int index = 0; index < manifest.parts.Length; index++)
                {
                    CityBusDriverManifestPart source = manifest.parts[index];
                    if (!renderersByName.TryGetValue(
                            source.name,
                            out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            $"Imported driver is missing renderer " +
                            $"'{source.name}'.");
                    }

                    renderer.sharedMaterials = new[] { sharedMaterial };
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.motionVectorGenerationMode =
                        MotionVectorGenerationMode.Object;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        skinned.updateWhenOffscreen = true;
                    }

                    bindings.Add(
                        new CityBusDriverRendererBinding(
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

                if (animator.avatar == null || !animator.avatar.isValid)
                {
                    throw new InvalidOperationException(
                        "Driver model has no valid Generic Avatar.");
                }

                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

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
                        "Imported driver geometry lost source bounds or " +
                        "triangle count.");
                }

                CityBusDriverAssetRegistry registry =
                    prefabRoot.AddComponent<CityBusDriverAssetRegistry>();
                registry.Configure(
                    animator,
                    model.transform,
                    renderers,
                    bindings.ToArray(),
                    RequireTransform(transformsByName, "pelvis", "driver prefab"),
                    RequireTransform(transformsByName, "spine", "driver prefab"),
                    RequireTransform(transformsByName, "chest", "driver prefab"),
                    RequireTransform(transformsByName, "neck", "driver prefab"),
                    RequireTransform(transformsByName, "head", "driver prefab"),
                    RequireTransform(transformsByName, "face.eye.L", "driver prefab"),
                    RequireTransform(transformsByName, "face.eye.R", "driver prefab"),
                    RequireTransform(transformsByName, "upper_arm.L", "driver prefab"),
                    RequireTransform(transformsByName, "forearm.L", "driver prefab"),
                    RequireTransform(transformsByName, "hand.L", "driver prefab"),
                    RequireTransform(transformsByName, "SOCKET_Grip.L", "driver prefab"),
                    RequireTransform(transformsByName, "thigh.L", "driver prefab"),
                    RequireTransform(transformsByName, "shin.L", "driver prefab"),
                    RequireTransform(transformsByName, "foot.L", "driver prefab"),
                    RequireTransform(transformsByName, "upper_arm.R", "driver prefab"),
                    RequireTransform(transformsByName, "forearm.R", "driver prefab"),
                    RequireTransform(transformsByName, "hand.R", "driver prefab"),
                    RequireTransform(transformsByName, "SOCKET_Grip.R", "driver prefab"),
                    RequireTransform(transformsByName, "thigh.R", "driver prefab"),
                    RequireTransform(transformsByName, "shin.R", "driver prefab"),
                    RequireTransform(transformsByName, "foot.R", "driver prefab"),
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
                        $"Could not save City bus driver prefab at " +
                        $"'{PrefabPath}'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        private static void ValidateRegistryBindings(
            CityBusDriverAssetRegistry registry)
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
                registry.LeftGripSocket,
                registry.LeftThigh,
                registry.LeftShin,
                registry.LeftFoot,
                registry.RightUpperArm,
                registry.RightForearm,
                registry.RightHand,
                registry.RightGripSocket,
                registry.RightThigh,
                registry.RightShin,
                registry.RightFoot
            };
            if (required.Any(target => target == null))
            {
                throw new InvalidOperationException(
                    "City bus driver registry is missing a procedural torso, " +
                    "eye, hand/grip or leg binding.");
            }

            if (registry.LeftGripSocket.parent != registry.LeftHand ||
                registry.RightGripSocket.parent != registry.RightHand ||
                registry.FaceEyeLeft.parent != registry.Head ||
                registry.FaceEyeRight.parent != registry.Head)
            {
                throw new InvalidOperationException(
                    "City bus driver eye/grip sockets lost their canonical " +
                    "head/hand parent hierarchy.");
            }
        }

        private static void ValidateEyeBindings(
            IReadOnlyList<CityBusDriverRendererBinding> bindings)
        {
            CityBusDriverRendererBinding leftEye = bindings.FirstOrDefault(
                binding => binding.RendererName == "FACE_EyeWhite.L");
            CityBusDriverRendererBinding rightEye = bindings.FirstOrDefault(
                binding => binding.RendererName == "FACE_EyeWhite.R");
            CityBusDriverRendererBinding leftPupil = bindings.FirstOrDefault(
                binding => binding.RendererName == "FACE_Pupil.L");
            CityBusDriverRendererBinding rightPupil = bindings.FirstOrDefault(
                binding => binding.RendererName == "FACE_Pupil.R");
            if (leftEye == null ||
                rightEye == null ||
                leftPupil == null ||
                rightPupil == null ||
                leftEye.Role != "long_horizontal_eye" ||
                rightEye.Role != "long_horizontal_eye" ||
                leftPupil.Role != "visible_eye_pupil" ||
                rightPupil.Role != "visible_eye_pupil" ||
                leftPupil.BoneName != "face.eye.L" ||
                rightPupil.BoneName != "face.eye.R")
            {
                throw new InvalidOperationException(
                    "Driver long-eye whites or independently poseable pupil " +
                    "bindings are invalid.");
            }
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
                        "Imported driver hierarchy contains duplicate " +
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
                    "City bus driver model contains no renderers.");
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
        private sealed class CityBusDriverManifest
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
            public bool lights;
            public bool rigidbodies;
            public int animation_count;
            public string[] animations;
            public string build_signature;
            public string head_design;
            public string eye_design;
            public CityBusDriverManifestBone[] bones;
            public CityBusDriverManifestPart[] parts;
        }

        [Serializable]
        private sealed class CityBusDriverManifestBone
        {
            public string name;
            public string parent;
        }

        [Serializable]
        private sealed class CityBusDriverManifestPart
        {
            public string name;
            public string role;
            public string bone;
            public string palette_name;
            public float[] base_color;
        }
    }
}
