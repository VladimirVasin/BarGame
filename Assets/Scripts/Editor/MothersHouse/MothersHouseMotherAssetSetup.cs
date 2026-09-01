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
    /// Imports the mother, builds her staged prefab and binds her provider.
    ///
    /// WHY SHE HAS HER OWN PIPELINE. She is built by the shared pedestrian
    /// body library and wears the shared <see cref="CityPedestrianAssetRegistry"/>,
    /// so on the face of it she belongs in
    /// <c>CityPedestrianAssetSetup</c> beside the other fourteen. Two things
    /// in her contract are not expressible there. A `PedestrianDescriptor`
    /// takes clip NAMES and reads them out of the one shared animation bank;
    /// hers is a bank of her own, because a rocking chair is not a shared
    /// beat. And every descriptor declares a walk - she has never walked and
    /// never will. Teaching that file a per-descriptor bank and an optional
    /// gait, to serve one character, would rewrite the contract fourteen
    /// working characters rest on. The arch-shelter residents and the
    /// mountain cafe cast stand apart for the same reason.
    ///
    /// WHAT IS DIFFERENT ABOUT HER FACE. Every other NPC in this game wears
    /// a detail atlas: light greys, baked into a sub-rectangle of the UVs,
    /// multiplied by a palette tint. One drawing forever. Hers is the hero's
    /// EXPRESSION atlas - full colour, a 4x4 grid, the cell chosen at runtime
    /// through `_BaseMap_ST`. The two are opposites and this file keeps them
    /// apart: a detail atlas wants a coloured tint, a face atlas demands a
    /// white one, and <see cref="ValidateFaceAtlas"/> refuses the mistake.
    /// </summary>
    [InitializeOnLoad]
    public static class MothersHouseMotherAssetSetup
    {
        public const string PlayerModelPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        public const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        public const string ModelPath =
            "Assets/Pedestrians/Staged/Models/Mother3D.fbx";
        public const string ManifestPath =
            "Assets/Pedestrians/Staged/Models/Mother3D.json";
        public const string PrefabPath =
            "Assets/Pedestrians/Staged/Prefabs/Mother3D.prefab";
        public const string FaceAtlasPath =
            "Assets/Pedestrians/Textures/MotherFaceAtlas.png";
        public const string AnimationPath =
            "Assets/Pedestrians/Animations/MothersHouseMother.fbx";
        public const string AnimationManifestPath =
            "Assets/Pedestrians/Animations/MothersHouseMother.json";
        public const string ProviderPath =
            "Assets/Resources/MothersHouse/MothersHouseMotherProvider.asset";

        public const string DesignId = "mother_v1";
        public const string ClipName = "MotherRock";
        public const string RootName = "Mother3D";
        public const string DisplayName = "Mother's house mother";
        public const string FaceSurfaceName = "GEO_FaceSurface";
        public const float ClipDuration = 6f;

        private const string Anatomy = "NpcHumanV2";
        private const int BoneCount = 31, Fps = 24, AtlasSize = 256;
        private const int FaceColumns = 4, FaceRows = 4, FaceCellSize = 64;
        private const int MinimumTriangles = 1700, MaximumTriangles = 2500;
        private const float Height = 1.75f;
        private const float RestPelvisHeight = 0.835f;
        private const float PositionTolerance = 0.0001f,
            RotationTolerance = 0.02f;
        private const float ClipTolerance = 0.002f,
            ColorTolerance = 0.0001f, UvTolerance = 0.0001f;

        /// <summary>
        /// The five cells the runtime may ask for. The atlas holds sixteen and
        /// the eleven spares repeat Neutral, so a later script that reaches
        /// past this list gets a calm face rather than a hole.
        /// </summary>
        private static readonly PlayerFacialExpression[] CanonicalExpressions =
        {
            PlayerFacialExpression.Neutral,
            PlayerFacialExpression.HalfBlink,
            PlayerFacialExpression.ClosedBlink,
            PlayerFacialExpression.Watchful,
            PlayerFacialExpression.Tense
        };

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static MothersHouseMotherAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/NPC Human V2/Build Mother's House Mother")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log("Mother's house mother prefab and provider rebuilt.");
        }

        [MenuItem("Bar Promenade/NPC Human V2/Validate Mother's House Mother")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("Mother's house mother asset contract is valid.");
        }

        public static bool SourcesExist()
        {
            return File.Exists(PlayerModelPath) &&
                   File.Exists(SharedMaterialPath) &&
                   File.Exists(ModelPath) &&
                   File.Exists(ManifestPath) &&
                   File.Exists(FaceAtlasPath) &&
                   File.Exists(AnimationPath) &&
                   File.Exists(AnimationManifestPath);
        }

        public static bool IsOwnedModelPath(string path)
        {
            return SamePath(path, ModelPath);
        }

        public static bool IsFaceAtlasPath(string path)
        {
            return SamePath(path, FaceAtlasPath);
        }

        public static bool IsOwnedSourcePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            // Hero V2 and Player3DLit are dependencies, not rebuild triggers.
            return SamePath(path, ModelPath) ||
                   SamePath(path, ManifestPath) ||
                   SamePath(path, FaceAtlasPath) ||
                   SamePath(path, AnimationPath) ||
                   SamePath(path, AnimationManifestPath);
        }

        public static bool TryGetClipLoopFlag(string name, out bool loop)
        {
            loop = string.Equals(name, ClipName, StringComparison.Ordinal);
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
                EnsureFolder(PrefabPath);
                Import(PlayerModelPath);
                Import(FaceAtlasPath);
                Import(ManifestPath);
                Import(ModelPath);
                Import(AnimationManifestPath);
                Import(AnimationPath);

                Material material = AssetDatabase.LoadAssetAtPath<Material>(
                    SharedMaterialPath);
                if (material == null)
                {
                    throw new InvalidOperationException(
                        "Missing shared Player3DLit material.");
                }

                BuildPrefab(LoadAnimations(), material);
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
            ModelManifest manifest = LoadModel();
            ValidateImportedModel(manifest);
            ValidateImportedAnimation();
            Texture2D atlas = LoadFaceAtlasTexture();
            GameObject prefab = LoadPrefab();
            CityPedestrianAssetRegistry registry =
                prefab.GetComponent<CityPedestrianAssetRegistry>();
            if (registry == null || registry.gameObject != prefab)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} needs one root registry.");
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
                    $"{DisplayName} Animator contract is stale.");
            }

            if (registry.ModelRoot == null ||
                registry.ModelRoot.parent != prefab.transform ||
                registry.ModelRoot.localPosition != Vector3.zero ||
                Quaternion.Angle(registry.ModelRoot.localRotation,
                    Quaternion.Euler(0f, 180f, 0f)) > RotationTolerance ||
                registry.ModelRoot.localScale != Vector3.one)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} staged model transform is stale.");
            }

            if (registry.DesignId != manifest.design_id ||
                registry.SourceTriangleCount != manifest.triangle_count ||
                registry.SourceGeneratorVersion != manifest.generator_version ||
                registry.BuildSignature != manifest.build_signature ||
                registry.PaletteVariant != 0 ||
                registry.HeadLamp != null ||
                registry.PreservesAirborneMotion ||
                registry.DetailAtlas != null)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} registry metadata is stale.");
            }

            // Her single clip rides the idle slot. The seated slot is the bus
            // ride's, and a design that is not on the bus must leave it empty;
            // the walk slot is empty because she does not have a walk.
            if (registry.SitClip != null || registry.ActionClip != null ||
                registry.WalkClip != null)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} may carry only her seated loop, in the " +
                    "idle slot.");
            }

            ValidateClip(registry.IdleClip, animations);
            ValidateAnchors(registry);
            ValidateBindings(prefab, registry, manifest);
            ValidateRegistryFaceAtlas(registry, manifest, atlas);

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
                    $"{DisplayName} registry bounds are stale.");
            }

            ValidatePassive(prefab, registry);
            ValidateProvider();
        }

        private static void BuildPrefab(
            AnimationManifest animations,
            Material material)
        {
            ModelManifest manifest = LoadModel();
            GameObject source =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Texture2D atlas = LoadFaceAtlasTexture();
            AnimationClip clip = LoadClip(animations);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not import {DisplayName}.");
            }

            var root = new GameObject(RootName);
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(source) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate {DisplayName}.");
                }

                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.SetLocalPositionAndRotation(
                    Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
                model.transform.localScale = Vector3.one;

                Dictionary<string, Renderer> renderersByName =
                    IndexRenderers(model);
                Dictionary<string, Transform> transforms =
                    IndexTransforms(model, DisplayName);
                if (renderersByName.Count != manifest.mesh_count)
                {
                    throw new InvalidOperationException(
                        $"{DisplayName} renderer count is stale.");
                }

                var renderers = new List<Renderer>(manifest.parts.Length);
                var bindings = new List<CityPedestrianRendererBinding>(
                    manifest.parts.Length);
                foreach (Part sourcePart in manifest.parts)
                {
                    if (!renderersByName.TryGetValue(
                            sourcePart.name, out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            $"{DisplayName} is missing renderer " +
                            $"'{sourcePart.name}'.");
                    }

                    renderer.sharedMaterials = new[] { material };
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.motionVectorGenerationMode =
                        MotionVectorGenerationMode.Object;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        skinned.updateWhenOffscreen = false;
                    }

                    // One colour in all four slots. The variants exist so a
                    // pool of anonymous walkers can wear four coats out of one
                    // model; there is exactly one of her, and a second palette
                    // would only be a second mother.
                    Color color = ParseColor(sourcePart.base_color);
                    renderers.Add(renderer);
                    bindings.Add(new CityPedestrianRendererBinding(
                        sourcePart.name,
                        sourcePart.role,
                        sourcePart.palette_name,
                        renderer,
                        color, color, color, color,
                        usesDetailAtlas: false));
                }

                Animator animator = RequireAnimator(model);
                Transform head =
                    RequireTransform(transforms, "head", DisplayName);
                Transform pelvis =
                    RequireTransform(transforms, "pelvis", DisplayName);
                Transform leftFoot =
                    RequireTransform(transforms, "foot.L", DisplayName);
                Transform rightFoot =
                    RequireTransform(transforms, "foot.R", DisplayName);

                Renderer[] rendererArray = renderers.ToArray();
                Bounds bounds = CalculateBounds(root.transform, rendererArray);
                ValidateGeometry(manifest, bounds, rendererArray);
                ValidateFaceUvs(renderersByName);

                CityPedestrianAssetRegistry registry =
                    root.AddComponent<CityPedestrianAssetRegistry>();
                registry.Configure(
                    animator,
                    model.transform,
                    rendererArray,
                    bindings.ToArray(),
                    head,
                    leftFoot,
                    rightFoot,
                    clip,
                    null,
                    bounds,
                    manifest.triangle_count,
                    manifest.generator_version,
                    manifest.design_id,
                    manifest.build_signature,
                    configuredPelvisAnchor: pelvis);
                registry.ConfigureFaceAtlas(
                    BuildFaceAtlasBinding(manifest, atlas, renderersByName));

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    root, PrefabPath, out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save '{PrefabPath}'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Turns the manifest's cell table into the runtime binding.
        ///
        /// THE ROWS ARE ALREADY UNITY'S. The generator paints top-down and
        /// flips each row into Unity's bottom-up order before writing the
        /// manifest, which is why nothing here subtracts from
        /// <see cref="FaceRows"/>. Flipping twice is the one mistake in this
        /// whole path that produces no error at all: every spare cell repeats
        /// Neutral, so a doubly-flipped grid still renders a face, and she
        /// would simply never change expression.
        /// </summary>
        private static Player3DFaceAtlasBinding BuildFaceAtlasBinding(
            ModelManifest manifest,
            Texture2D atlas,
            IReadOnlyDictionary<string, Renderer> renderers)
        {
            FaceAtlas source = ValidateFaceAtlas(manifest);
            if (!renderers.TryGetValue(source.renderer, out Renderer renderer))
            {
                throw new InvalidOperationException(
                    $"{DisplayName} has no '{source.renderer}' renderer.");
            }

            var cells = new List<Player3DFaceAtlasCell>(source.cells.Length);
            foreach (FaceAtlasCell cell in source.cells)
            {
                cells.Add(new Player3DFaceAtlasCell(
                    ParseExpression(cell.expression), cell.column, cell.row));
            }

            var binding = new Player3DFaceAtlasBinding(
                renderer, atlas, source.columns, source.rows, cells.ToArray());
            if (!binding.IsConfigured)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} face atlas binding is incomplete.");
            }

            return binding;
        }

        private static Animator RequireAnimator(GameObject model)
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
                    $"{DisplayName} contains more than one Animator.");
            }

            if (animator.avatar == null)
            {
                animator.avatar = avatar;
            }

            if (avatar == null || !avatar.isValid || animator.avatar != avatar)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} does not use Hero/NPC V2 Avatar.");
            }

            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return animator;
        }

        private static void BindProvider()
        {
            MothersHouseMotherProvider provider =
                AssetDatabase.LoadAssetAtPath<MothersHouseMotherProvider>(
                    ProviderPath);
            if (provider == null)
            {
                provider = ScriptableObject
                    .CreateInstance<MothersHouseMotherProvider>();
                AssetDatabase.CreateAsset(provider, ProviderPath);
            }

            provider.Configure(LoadPrefab());
            EditorUtility.SetDirty(provider);
        }

        private static void ValidateAnchors(
            CityPedestrianAssetRegistry registry)
        {
            Transform[] actual =
            {
                registry.HeadAnchor, registry.PelvisAnchor,
                registry.LeftFootAnchor, registry.RightFootAnchor
            };
            string[] names = { "head", "pelvis", "foot.L", "foot.R" };
            for (int index = 0; index < names.Length; index++)
            {
                if (actual[index] == null ||
                    !actual[index].IsChildOf(registry.ModelRoot) ||
                    actual[index].name != names[index])
                {
                    throw new InvalidOperationException(
                        $"{DisplayName} anchor '{names[index]}' is missing " +
                        "or stale.");
                }
            }
        }

        private static void ValidateBindings(
            GameObject prefab,
            CityPedestrianAssetRegistry registry,
            ModelManifest manifest)
        {
            Renderer[] renderers =
                prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != manifest.mesh_count ||
                registry.RendererBindings.Count != manifest.mesh_count ||
                registry.Renderers.Count != manifest.mesh_count)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} renderer count is stale.");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                SharedMaterialPath);
            var seen = new HashSet<Renderer>();
            var byName =
                new Dictionary<string, Renderer>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.parts.Length; index++)
            {
                Part source = manifest.parts[index];
                CityPedestrianRendererBinding binding =
                    registry.RendererBindings[index];
                Color expected = ParseColor(source.base_color);
                if (binding == null || binding.Renderer == null ||
                    !seen.Add(binding.Renderer) ||
                    !byName.TryAdd(binding.Renderer.name, binding.Renderer) ||
                    binding.Renderer.name != source.name ||
                    binding.RendererName != source.name ||
                    binding.Role != source.role ||
                    binding.PaletteName != source.palette_name ||
                    !SameColor(binding.BaseColor, expected) ||
                    !SameColor(binding.VariantOneColor, expected) ||
                    !SameColor(binding.VariantTwoColor, expected) ||
                    !SameColor(binding.VariantThreeColor, expected) ||
                    binding.UsesDetailAtlas ||
                    binding.Renderer.sharedMaterials.Length != 1 ||
                    binding.Renderer.sharedMaterial != material ||
                    binding.Renderer.shadowCastingMode !=
                        ShadowCastingMode.On ||
                    !binding.Renderer.receiveShadows)
                {
                    throw new InvalidOperationException(
                        $"{DisplayName} binding {index} is stale.");
                }
            }

            if (CountTriangles(renderers) != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} triangle count is stale.");
            }

            ValidateFaceUvs(byName);
        }

        private static void ValidateRegistryFaceAtlas(
            CityPedestrianAssetRegistry registry,
            ModelManifest manifest,
            Texture2D atlas)
        {
            FaceAtlas source = ValidateFaceAtlas(manifest);
            Player3DFaceAtlasBinding binding = registry.FaceAtlas;
            if (!registry.HasFaceAtlas || binding == null ||
                !binding.IsConfigured || binding.Texture != atlas ||
                binding.Renderer == null ||
                binding.Renderer.name != source.renderer ||
                binding.Columns != source.columns ||
                binding.Rows != source.rows ||
                binding.Cells.Count != source.cells.Length)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} face atlas binding is stale.");
            }

            for (int index = 0; index < source.cells.Length; index++)
            {
                FaceAtlasCell expected = source.cells[index];
                Player3DFaceAtlasCell actual = binding.Cells[index];
                if (actual.Expression != ParseExpression(expected.expression) ||
                    actual.Column != expected.column ||
                    actual.Row != expected.row)
                {
                    throw new InvalidOperationException(
                        $"{DisplayName} face cell {index} is stale.");
                }
            }
        }

        private static void ValidatePassive(
            GameObject prefab,
            CityPedestrianAssetRegistry registry)
        {
            // The room holds exactly three AudioSources and a counted set of
            // colliders; her prefab must add to neither. She is also silent by
            // canon, which is the same requirement arriving from the other
            // direction.
            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Collider2D>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody2D>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AudioSource>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Camera>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} prefab must remain passive.");
            }

            MonoBehaviour[] behaviours =
                prefab.GetComponentsInChildren<MonoBehaviour>(true);
            if (behaviours.Length != 1 || behaviours[0] != registry)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} may carry only her registry.");
            }
        }

        private static void ValidateProvider()
        {
            MothersHouseMotherProvider provider =
                AssetDatabase.LoadAssetAtPath<MothersHouseMotherProvider>(
                    ProviderPath);
            if (provider == null)
            {
                throw new InvalidOperationException(
                    "Missing mother's house mother provider.");
            }

            provider.ValidateOrThrow();
            GameObject prefab = LoadPrefab();
            if (provider.StagedPrefab != prefab ||
                AssetDatabase.GetAssetPath(prefab).IndexOf(
                    "/Resources/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException(
                    "Provider binding is stale or Resources-resident.");
            }
        }

        private static ModelManifest LoadModel()
        {
            if (!ModelPath.StartsWith(
                    "Assets/Pedestrians/Staged/Models/",
                    StringComparison.OrdinalIgnoreCase) ||
                !PrefabPath.StartsWith(
                    "Assets/Pedestrians/Staged/Prefabs/",
                    StringComparison.OrdinalIgnoreCase) ||
                PrefabPath.IndexOf(
                    "/Resources/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                CityPedestrianResources.TryGetArchetype(DesignId, out _))
            {
                throw new InvalidOperationException(
                    $"{DisplayName} is not isolated from the pool.");
            }

            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
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
                    $"{DisplayName} manifest is malformed.");
            }

            if (manifest.design_id != DesignId ||
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
                    $"{DisplayName} anatomy or geometry is invalid.");
            }

            if (!manifest.staged || manifest.pool_eligible ||
                manifest.emissive || manifest.colliders ||
                manifest.rides_bus ||
                manifest.animation_count != 0 ||
                manifest.animations.Length != 0 ||
                manifest.material_asset != SharedMaterialPath ||
                manifest.shared_animation_source != AnimationPath ||
                !manifest.shared_clips.SequenceEqual(
                    new[] { ClipName }, StringComparer.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.generator_version) ||
                manifest.build_signature?.Length != 64 ||
                (manifest.signature_effects ?? Array.Empty<string>()).Length !=
                    0 ||
                (manifest.rig_anchors ?? Array.Empty<RigAnchor>()).Length != 0)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} staged/source contract is invalid.");
            }

            // She wears a face atlas INSTEAD of a detail atlas, never both:
            // one is a full-colour grid selected at runtime, the other a grey
            // mask baked into the UVs, and a mesh cannot sample two textures
            // through one shared material.
            if ((manifest.texture_bindings ??
                 Array.Empty<TextureBinding>()).Length != 0)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} must carry no detail atlas.");
            }

            HashSet<string> partNames = ValidateHierarchy(manifest);
            if (!partNames.Contains(FaceSurfaceName))
            {
                throw new InvalidOperationException(
                    $"{DisplayName} is missing '{FaceSurfaceName}'.");
            }

            ValidateFaceAtlas(manifest);
            return manifest;
        }

        private static HashSet<string> ValidateHierarchy(ModelManifest manifest)
        {
            var bones = new HashSet<string>(StringComparer.Ordinal);
            foreach (Bone bone in manifest.bones)
            {
                if (bone == null || string.IsNullOrEmpty(bone.name) ||
                    !bones.Add(bone.name))
                {
                    throw new InvalidOperationException(
                        $"{DisplayName} has duplicate/missing bones.");
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
                    !string.IsNullOrEmpty(part.atlas_region) ||
                    !parts.Add(part.name) || !bones.Contains(part.bone))
                {
                    throw new InvalidOperationException(
                        $"{DisplayName} has an invalid mesh binding.");
                }
            }

            return parts;
        }

        /// <summary>
        /// Checks the expression grid, and one thing that is easy to miss.
        ///
        /// THE FACE PATCH MUST BE TINTED WHITE. `_BaseColor` multiplies the
        /// sampled texture, and this atlas is painted in finished skin, not in
        /// the light greys a detail atlas uses. Tinting the patch with her
        /// complexion applies it a second time and hands back a face at about
        /// a quarter brightness - and it fails quietly, because every render
        /// out of Blender looks right and only the game looks muddy.
        /// </summary>
        private static FaceAtlas ValidateFaceAtlas(ModelManifest manifest)
        {
            FaceAtlas atlas = manifest.face_atlas;
            if (atlas == null || atlas.cells == null ||
                atlas.texture_asset != FaceAtlasPath ||
                atlas.renderer != FaceSurfaceName ||
                atlas.columns != FaceColumns || atlas.rows != FaceRows ||
                atlas.cell_size_px != FaceCellSize ||
                atlas.width_px != AtlasSize || atlas.height_px != AtlasSize ||
                atlas.color_space != "sRGB" ||
                atlas.filter_mode != "Point" ||
                atlas.wrap_mode != "Clamp" || atlas.mipmaps ||
                atlas.compression != "Uncompressed" ||
                atlas.uv_channel != 0 ||
                atlas.uv_origin != "bottom_left" ||
                atlas.material_tint_hex != "FFFFFF" ||
                atlas.uv_contract != "local_0_1_runtime_cell_scale_offset" ||
                atlas.sha256?.Length != 64 ||
                !string.Equals(atlas.sha256, FileSha(FaceAtlasPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{DisplayName} face atlas contract is invalid.");
            }

            Part face = manifest.parts.FirstOrDefault(
                part => part.name == FaceSurfaceName);
            if (face == null || face.role != "facial_atlas" ||
                face.bone != "head" ||
                !SameColor(ParseColor(face.base_color), Color.white))
            {
                throw new InvalidOperationException(
                    $"'{FaceSurfaceName}' must be a head-bound facial atlas " +
                    "part tinted white; a coloured tint would multiply her " +
                    "complexion into an already-painted face.");
            }

            var expressions = new HashSet<PlayerFacialExpression>();
            foreach (FaceAtlasCell cell in atlas.cells)
            {
                if (cell == null ||
                    !expressions.Add(ParseExpression(cell.expression)) ||
                    cell.column < 0 || cell.column >= atlas.columns ||
                    cell.row < 0 || cell.row >= atlas.rows)
                {
                    throw new InvalidOperationException(
                        $"{DisplayName} has an invalid face cell.");
                }
            }

            if (!CanonicalExpressions.All(expressions.Contains) ||
                expressions.Count != CanonicalExpressions.Length)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} face atlas must name all five canonical " +
                    "expressions and nothing else.");
            }

            return atlas;
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
                manifest.mesh_count != 0 || manifest.clip_count != 1 ||
                manifest.clips.Length != 1 || manifest.root_motion ||
                manifest.anatomy_standard != Anatomy ||
                Mathf.Abs(manifest.rest_pelvis_height_m - RestPelvisHeight) >
                    PositionTolerance ||
                string.IsNullOrWhiteSpace(manifest.skeleton_source) ||
                string.IsNullOrWhiteSpace(manifest.generator_version) ||
                manifest.build_signature?.Length != 64)
            {
                throw new InvalidOperationException(
                    "Mother's animation manifest is invalid.");
            }

            Clip clip = manifest.clips[0];
            if (clip == null || clip.name != ClipName ||
                clip.archetype != DesignId ||
                Mathf.Abs(clip.duration_seconds - ClipDuration) >
                    ClipTolerance ||
                clip.frame_start != 0 ||
                clip.frame_end != Mathf.RoundToInt(ClipDuration * Fps) ||
                !clip.loop || clip.one_shot || !clip.in_place ||
                !clip.seated || !clip.perched ||
                clip.keyed_bone_count != BoneCount ||
                string.IsNullOrWhiteSpace(clip.authored_posture) ||
                string.IsNullOrWhiteSpace(clip.gait) ||
                Mathf.Abs(clip.loop_max_error) > PositionTolerance ||
                clip.root_translation_range_m == null ||
                clip.root_translation_range_m.Length != 3 ||
                clip.root_translation_range_m.Any(value =>
                    Mathf.Abs(value) > PositionTolerance))
            {
                throw new InvalidOperationException(
                    "Mother's clip contract is invalid.");
            }

            ValidateSeat(clip);
            return manifest;
        }

        /// <summary>
        /// Cross-checks the measured seat against the chair she sits in.
        ///
        /// The generator measures where her hips end up over her own soles and
        /// prints it; the chair's cushion is drawn at a fixed height in the
        /// room. Those two numbers are authored in different files by
        /// different tools, and nothing but this check would notice them
        /// drifting apart - the failure is a woman floating a finger's width
        /// above her own chair, which reads as a bad pose rather than as a
        /// broken contract.
        /// </summary>
        private static void ValidateSeat(Clip clip)
        {
            float cushion = MothersHouseMotherPresentation.CushionTopY;
            if (clip.perch_seat_height_min_m > clip.perch_seat_height_max_m ||
                Mathf.Abs(clip.perch_seat_height_min_m - cushion) > 0.01f ||
                Mathf.Abs(clip.perch_seat_height_max_m - cushion) > 0.01f)
            {
                throw new InvalidOperationException(
                    $"Her seat sits {clip.perch_seat_height_min_m:0.0000} m " +
                    $"over her soles, but the cushion is drawn at {cushion} m.");
            }

            if (Mathf.Abs(clip.perch_pelvis_lift_m -
                    MothersHouseMotherPresentation.PerchPelvisLiftMeters) >
                0.0005f)
            {
                throw new InvalidOperationException(
                    "Her presentation carries a stale pelvis lift: the clip " +
                    $"measures {clip.perch_pelvis_lift_m:0.0000} m.");
            }

            if (clip.perch_contact_parts == null ||
                clip.perch_contact_parts.Length == 0)
            {
                throw new InvalidOperationException(
                    "Her seated loop touches the floor with nothing.");
            }
        }

        private static void ValidateImportedAnimation()
        {
            Avatar avatar = FindAvatar();
            ModelImporter importer =
                AssetImporter.GetAtPath(AnimationPath) as ModelImporter;
            AnimationClip[] clips = ImportedClips();
            GameObject source =
                AssetDatabase.LoadAssetAtPath<GameObject>(AnimationPath);
            if (avatar == null || !avatar.isValid || importer == null ||
                !importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.Generic ||
                importer.avatarSetup !=
                    ModelImporterAvatarSetup.CopyFromOther ||
                importer.sourceAvatar != avatar ||
                importer.materialImportMode !=
                    ModelImporterMaterialImportMode.None ||
                clips.Length != 1 || source == null ||
                source.GetComponentsInChildren<Renderer>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Imported mother animation bank is invalid.");
            }

            Transform root = RequireTransform(
                IndexTransforms(source, "mother animation"),
                "root", "mother animation");
            if (root.GetComponentsInChildren<Transform>(true).Length !=
                BoneCount)
            {
                throw new InvalidOperationException(
                    "Mother animation bank has the wrong skeleton.");
            }
        }

        private static void ValidateImportedModel(ModelManifest manifest)
        {
            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            GameObject player =
                AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
            if (model == null || player == null)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} or Hero V2 failed to import.");
            }

            Dictionary<string, Transform> modelTransforms =
                IndexTransforms(model, DisplayName);
            Dictionary<string, Transform> playerTransforms =
                IndexTransforms(player, "Hero V2");
            foreach (Bone bone in manifest.bones)
            {
                Transform actual = RequireTransform(
                    modelTransforms, bone.name, DisplayName);
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
                        $"{DisplayName} bone '{bone.name}' is stale.");
                }
            }

            Transform rig =
                RequireTransform(modelTransforms, "root", DisplayName);
            Renderer[] renderers =
                model.GetComponentsInChildren<Renderer>(true);
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Avatar avatar = FindAvatar();
            bool hasClips = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<AnimationClip>().Any(clip =>
                    !clip.name.StartsWith(
                        "__preview__", StringComparison.Ordinal));
            if (rig.GetComponentsInChildren<Transform>(true).Length !=
                    BoneCount ||
                renderers.Length != manifest.mesh_count ||
                CountTriangles(renderers) != manifest.triangle_count ||
                hasClips || avatar == null || !avatar.isValid ||
                importer == null || importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.Generic ||
                importer.avatarSetup !=
                    ModelImporterAvatarSetup.CopyFromOther ||
                importer.sourceAvatar != avatar ||
                importer.materialImportMode !=
                    ModelImporterMaterialImportMode.None)
            {
                throw new InvalidOperationException(
                    $"Imported {DisplayName} contract is invalid.");
            }
        }

        private static Texture2D LoadFaceAtlasTexture()
        {
            Texture2D atlas =
                AssetDatabase.LoadAssetAtPath<Texture2D>(FaceAtlasPath);
            TextureImporter importer =
                AssetImporter.GetAtPath(FaceAtlasPath) as TextureImporter;
            if (atlas == null || atlas.width != AtlasSize ||
                atlas.height != AtlasSize ||
                atlas.filterMode != FilterMode.Point ||
                atlas.wrapMode != TextureWrapMode.Clamp ||
                atlas.mipmapCount != 1 ||
                importer == null || !importer.sRGBTexture ||
                importer.isReadable ||
                importer.filterMode != FilterMode.Point ||
                importer.wrapMode != TextureWrapMode.Clamp ||
                importer.mipmapEnabled || importer.streamingMipmaps ||
                importer.maxTextureSize != AtlasSize ||
                importer.textureCompression !=
                    TextureImporterCompression.Uncompressed ||
                importer.alphaIsTransparency)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} face atlas import contract is invalid.");
            }

            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");
            if (!standalone.overridden ||
                standalone.maxTextureSize != AtlasSize ||
                standalone.textureCompression !=
                    TextureImporterCompression.Uncompressed ||
                standalone.crunchedCompression)
            {
                // A compressed expression grid bleeds neighbouring cells into
                // each other at the seams, which on a 64 px face is an eye
                // from the next expression.
                throw new InvalidOperationException(
                    $"{DisplayName} face atlas is compressed on Standalone.");
            }

            return atlas;
        }

        private static AnimationClip LoadClip(AnimationManifest manifest)
        {
            AnimationClip clip = ImportedClips().FirstOrDefault(candidate =>
                NormalizeClip(candidate.name) == ClipName);
            ValidateClip(clip, manifest);
            return clip;
        }

        private static void ValidateClip(
            AnimationClip clip,
            AnimationManifest manifest)
        {
            Clip source = manifest.clips.FirstOrDefault(candidate =>
                candidate != null && candidate.name == ClipName);
            AnimationClipSettings settings = clip == null
                ? null
                : AnimationUtility.GetAnimationClipSettings(clip);
            if (clip == null || source == null ||
                NormalizeClip(clip.name) != ClipName ||
                Mathf.Abs(clip.length - ClipDuration) > ClipTolerance ||
                settings == null || !settings.loopTime || !settings.loopBlend ||
                AnimationUtility.GetAnimationEvents(clip).Length != 0)
            {
                throw new InvalidOperationException(
                    $"Imported clip '{ClipName}' is invalid.");
            }
        }

        private static void ValidateGeometry(
            ModelManifest manifest,
            Bounds bounds,
            Renderer[] renderers)
        {
            if (Mathf.Abs(bounds.size.y - manifest.height_m) > 0.035f ||
                Mathf.Abs(bounds.min.y) > 0.025f ||
                CountTriangles(renderers) != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    $"{DisplayName} bounds/triangles are invalid.");
            }
        }

        /// <summary>
        /// The face patch owns the WHOLE texture square, not a sub-rectangle.
        ///
        /// This is the difference from every other atlas in the project. A
        /// detail-atlas part is baked into its region and samples one drawing
        /// forever; the face patch keeps a raw 0..1 UV so `_BaseMap_ST` can
        /// slide it onto any cell of the grid at runtime. UVs authored into a
        /// cell would let the atlas work exactly once - on whichever cell they
        /// were baked into - and then slide off the grid entirely.
        /// </summary>
        private static void ValidateFaceUvs(
            IReadOnlyDictionary<string, Renderer> renderers)
        {
            if (!renderers.TryGetValue(FaceSurfaceName, out Renderer renderer))
            {
                throw new InvalidOperationException(
                    $"Missing '{FaceSurfaceName}' renderer.");
            }

            Mesh mesh = RendererMesh(renderer);
            Vector2[] uv = mesh?.uv;
            if (mesh == null || uv == null || uv.Length != mesh.vertexCount ||
                uv.Length == 0)
            {
                throw new InvalidOperationException(
                    $"'{FaceSurfaceName}' has no valid UV0.");
            }

            Vector2 min = uv[0];
            Vector2 max = uv[0];
            foreach (Vector2 point in uv)
            {
                if (point.x < -UvTolerance || point.x > 1f + UvTolerance ||
                    point.y < -UvTolerance || point.y > 1f + UvTolerance)
                {
                    throw new InvalidOperationException(
                        $"'{FaceSurfaceName}' UV0 leaves the unit square.");
                }

                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            if (Mathf.Abs(min.x) > UvTolerance ||
                Mathf.Abs(min.y) > UvTolerance ||
                Mathf.Abs(max.x - 1f) > UvTolerance ||
                Mathf.Abs(max.y - 1f) > UvTolerance)
            {
                throw new InvalidOperationException(
                    $"'{FaceSurfaceName}' UV0 must span exactly 0..1 so a " +
                    "runtime cell transform lands on a whole cell.");
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
                        (corner & 1) == 0
                            ? mesh.bounds.min.x : mesh.bounds.max.x,
                        (corner & 2) == 0
                            ? mesh.bounds.min.y : mesh.bounds.max.y,
                        (corner & 4) == 0
                            ? mesh.bounds.min.z : mesh.bounds.max.z));
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
                throw new InvalidOperationException("The mother has no renderers.");
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
            var result =
                new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform item in
                     root.GetComponentsInChildren<Transform>(true))
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
            GameObject root)
        {
            var result =
                new Dictionary<string, Renderer>(StringComparer.Ordinal);
            foreach (Renderer item in
                     root.GetComponentsInChildren<Renderer>(true))
            {
                if (!result.TryAdd(item.name, item))
                {
                    throw new InvalidOperationException(
                        $"{DisplayName} duplicates renderer '{item.name}'.");
                }
            }

            return result;
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
                $"{label} is missing transform '{name}'.");
        }

        private static GameObject LoadPrefab()
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            return prefab != null
                ? prefab
                : throw new InvalidOperationException(
                    $"Missing prefab '{PrefabPath}'.");
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

        private static PlayerFacialExpression ParseExpression(string name)
        {
            if (Enum.TryParse(name, false, out PlayerFacialExpression result) &&
                Enum.IsDefined(typeof(PlayerFacialExpression), result))
            {
                return result;
            }

            throw new InvalidOperationException(
                $"'{name}' is not a canonical facial expression.");
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
            return string.Equals(
                left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static void RequireSources()
        {
            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "Mother's house mother sources are incomplete.");
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
            string directory =
                Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
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
                Debug.LogError("Mother validation failed: " + exception);
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
                Debug.LogError("Mother build failed: " + exception);
            }
        }

        [Serializable]
        private sealed class ModelManifest
        {
            public string generator_version, design_id, anatomy_standard;
            public float rest_pelvis_height_m, height_m;
            public string pose, forward_axis, anatomical_left_axis;
            public int mesh_count, triangle_count, animation_count;
            public int[] triangle_budget;
            public bool staged, pool_eligible, emissive, colliders, rides_bus;
            public string material_asset, shared_animation_source;
            public string build_signature;
            public string[] animations, shared_clips, signature_effects;
            public Bone[] bones;
            public Part[] parts;
            public RigAnchor[] rig_anchors;
            public TextureBinding[] texture_bindings;
            public FaceAtlas face_atlas;
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
            public string texture_asset;
        }

        [Serializable]
        private sealed class FaceAtlas
        {
            public string texture_asset, renderer, sha256, color_space;
            public string filter_mode, wrap_mode, compression, uv_origin;
            public string material_tint_hex, uv_contract;
            public int columns, rows, cell_size_px, width_px, height_px;
            public int uv_channel;
            public bool mipmaps;
            public FaceAtlasCell[] cells;
        }

        [Serializable]
        private sealed class FaceAtlasCell
        {
            public string expression;
            public int column, row;
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
            public float duration_seconds, loop_max_error;
            public int frame_start, frame_end, keyed_bone_count;
            public bool loop, one_shot, in_place, seated, perched;
            public float perch_seat_height_min_m, perch_seat_height_max_m;
            public float perch_pelvis_lift_m, seated_drop_m;
            public string[] perch_contact_parts;
            public float[] root_translation_range_m;
        }
    }

    public sealed class MothersHouseMotherModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!(assetImporter is ModelImporter importer))
            {
                return;
            }

            bool model =
                MothersHouseMotherAssetSetup.IsOwnedModelPath(assetPath);
            bool animation = string.Equals(
                assetPath,
                MothersHouseMotherAssetSetup.AnimationPath,
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
            importer.materialImportMode =
                ModelImporterMaterialImportMode.None;
            if (animation)
            {
                ConfigureClips(importer);
            }
        }

        private void OnPreprocessAnimation()
        {
            if (assetImporter is ModelImporter importer &&
                string.Equals(
                    assetPath,
                    MothersHouseMotherAssetSetup.AnimationPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                ConfigureClips(importer);
            }
        }

        private static void ConfigureClips(ModelImporter importer)
        {
            ModelImporterClipAnimation[] clips =
                importer.defaultClipAnimations;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.name = Normalize(clip.name);
                if (!MothersHouseMotherAssetSetup.TryGetClipLoopFlag(
                        clip.name, out bool loop) || !names.Add(clip.name))
                {
                    throw new InvalidOperationException(
                        $"Undeclared/duplicate mother clip '{clip.name}'.");
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
                    MothersHouseMotherAssetSetup.PlayerModelPath)
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
                    MothersHouseMotherAssetSetup.IsOwnedSourcePath))
            {
                MothersHouseMotherAssetSetup.QueueBuildWhenSourcesExist();
            }
        }
    }

    public sealed class MothersHouseMotherTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (assetImporter is TextureImporter importer &&
                MothersHouseMotherAssetSetup.IsFaceAtlasPath(assetPath))
            {
                // The hero's own atlas flags, deliberately shared rather than
                // re-derived: point filtering, clamp, no mipmaps, no
                // compression. A second family drifting from the first by one
                // flag is how a pixel-art face turns soft.
                Player3DV2TextureImporter.ConfigureAtlas(importer);
            }
        }
    }
}
