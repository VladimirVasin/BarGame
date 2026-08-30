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
    public static class Player3DAssetSetup
    {
        public const string ModelPath =
            "Assets/Player3D/Models/PlayerCharacter3D.fbx";
        public const string ManifestPath =
            "Assets/Player3D/Models/PlayerCharacter3D.json";
        public const string AnimationFolderPath =
            "Assets/Player3D/Animations";
        public const string MaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        public const string PrefabPath =
            "Assets/Resources/Player/Player3D.prefab";

        private const int BuildSchemaVersion = 2;
        private const string FrozenProductionBuildSignature =
            "4b1975d20bc32bccd0c0b8894609744b";
        private const string SetupScriptPath =
            "Assets/Scripts/Editor/Player3D/Player3DAssetSetup.cs";
        private const string ImporterScriptPath =
            "Assets/Scripts/Editor/Player3D/Player3DModelImporter.cs";
        private const string RegistryScriptPath =
            "Assets/Scripts/Runtime/Player3D/Player3DAssetRegistry.cs";
        private const float ExpectedHeight = 1.75f;
        private const int MaximumTriangleCount = 4500;

        // Hero V1 is the user's preserved production fallback while Hero V2
        // is evaluated in parallel. The optional V2 registry schema is not a
        // reason to reserialize the frozen V1 prefab. If any actual V1 source
        // or its importer changes, this integrity check stops matching and
        // the ordinary dependency-stamp rebuild resumes.
        private static readonly IReadOnlyDictionary<string, string>
            FrozenProductionSourceSha256 =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                {
                    ModelPath,
                    "301CBEB458FA6F35BEA2D750F09E64BA86D2BFD8BC457909E850BED55028965D"
                },
                {
                    ManifestPath,
                    "F3BC5C325F8FA17FD53179DD6D0EEDD09EEE2E9CCE4FA93C604D0C7B12006544"
                },
                {
                    "Assets/Player3D/Animations/PlayerCharacter3DAnimations.fbx",
                    "B1ABD2956B5980070924200AF3804102FBB7EF8F92E85AFD76C76CDF2ADD8B45"
                },
                {
                    ImporterScriptPath,
                    "9D5B85583833021C8003C3A24CCD7604832F517C0CCBD5E1772420156C5A470B"
                }
            };

        private static readonly IReadOnlyDictionary<string, string>
            PaletteHex = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "MAT_Skin", "AE8D7B" },
                { "MAT_SkinShadow", "67504B" },
                { "MAT_SkinDark", "392D2E" },
                { "MAT_Hair", "08080B" },
                { "MAT_HairHighlight", "202129" },
                { "MAT_Jacket", "542B38" },
                { "MAT_JacketDark", "311A25" },
                { "MAT_JacketEdge", "704050" },
                { "MAT_Shirt", "2A2D30" },
                { "MAT_Jeans", "151C2A" },
                { "MAT_JeansEdge", "26334A" },
                { "MAT_BootLeather", "1E1A16" },
                { "MAT_BootSole", "09090A" },
                { "MAT_Bandage", "B6A899" },
                { "MAT_BandageDark", "746B61" },
                { "MAT_Patch", "99743A" },
                { "MAT_Strap", "211B18" },
                { "MAT_StrapEdge", "433127" },
                { "MAT_EyeWhite", "8F8780" },
                { "MAT_Eye", "141317" },
                { "MAT_Metal", "58514A" }
            };

        private static readonly IReadOnlyDictionary<
            string,
            Player3DAnatomicalPart> RequiredBodyParts =
            new Dictionary<string, Player3DAnatomicalPart>(StringComparer.Ordinal)
            {
                { "GEO_Head", Player3DAnatomicalPart.Head },
                { "GEO_Neck", Player3DAnatomicalPart.Neck },
                { "GEO_Torso", Player3DAnatomicalPart.Torso },
                { "GEO_Pelvis", Player3DAnatomicalPart.Pelvis },
                { "GEO_UpperArm.L", Player3DAnatomicalPart.LeftUpperArm },
                { "GEO_Forearm.L", Player3DAnatomicalPart.LeftForearm },
                { "GEO_Hand.L", Player3DAnatomicalPart.LeftHand },
                { "GEO_UpperArm.R", Player3DAnatomicalPart.RightUpperArm },
                { "GEO_Forearm.R", Player3DAnatomicalPart.RightForearm },
                { "GEO_Hand.R", Player3DAnatomicalPart.RightHand },
                { "GEO_Thigh.L", Player3DAnatomicalPart.LeftThigh },
                { "GEO_Shin.L", Player3DAnatomicalPart.LeftShin },
                { "GEO_Foot.L", Player3DAnatomicalPart.LeftFoot },
                { "GEO_Thigh.R", Player3DAnatomicalPart.RightThigh },
                { "GEO_Shin.R", Player3DAnatomicalPart.RightShin },
                { "GEO_Foot.R", Player3DAnatomicalPart.RightFoot }
            };

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static Player3DAssetSetup()
        {
            // Asset import workers and command-line test runs also execute
            // InitializeOnLoad constructors. They must never enqueue an asset
            // mutation while their parent process is compiling or refreshing.
            // Batch callers that need a rebuild invoke BuildOrThrow directly.
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidatePrefabDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/Player 3D/Build Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log(
                $"Player 3D runtime prefab rebuilt at '{PrefabPath}'.");
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) && File.Exists(ManifestPath);
        }

        private static void ValidatePrefabDependencyStamp()
        {
            if (!SourcesExist())
            {
                return;
            }

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Player3DAssetRegistry registry = prefab != null
                ? prefab.GetComponent<Player3DAssetRegistry>()
                : null;
            if (registry != null &&
                (string.Equals(
                     registry.BuildSignature,
                     CalculateBuildSignature(),
                     StringComparison.Ordinal) ||
                 IsFrozenProductionPrefabIntact(registry)))
            {
                return;
            }

            QueueBuildWhenSourcesExist();
        }

        private static bool IsFrozenProductionPrefabIntact(
            Player3DAssetRegistry registry)
        {
            if (!string.Equals(
                    registry.BuildSignature,
                    FrozenProductionBuildSignature,
                    StringComparison.Ordinal))
            {
                return false;
            }

            foreach (KeyValuePair<string, string> source in
                     FrozenProductionSourceSha256)
            {
                if (!File.Exists(source.Key) ||
                    !string.Equals(
                        CalculateFileSha256(source.Key),
                        source.Value,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string CalculateFileSha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream))
                    .Replace("-", string.Empty);
            }
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
                    "Player 3D source FBX and manifest must both exist at " +
                    $"'{ModelPath}' and '{ManifestPath}'.");
            }

            isBuilding = true;
            try
            {
                EnsureFolderForAsset(MaterialPath);
                EnsureFolderForAsset(PrefabPath);
                AssetDatabase.ImportAsset(
                    ModelPath,
                    ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(
                    ManifestPath,
                    ImportAssetOptions.ForceUpdate);
                ImportAnimationSources();

                Player3DManifest manifest = LoadAndValidateManifest();
                GameObject modelAsset =
                    AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (modelAsset == null)
                {
                    throw new InvalidOperationException(
                        $"Unity did not import a model from '{ModelPath}'.");
                }

                Material sharedMaterial = EnsureSharedMaterial();
                Player3DAnimationBinding[] animations =
                    LoadAnimationBindings(manifest);
                BuildPrefab(
                    modelAsset,
                    manifest,
                    sharedMaterial,
                    animations);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                isBuilding = false;
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
                    $"Could not build Player 3D runtime prefab: {exception}");
            }
        }

        private static Player3DManifest LoadAndValidateManifest()
        {
            TextAsset text =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (text == null)
            {
                throw new InvalidOperationException(
                    $"Could not import Player 3D manifest '{ManifestPath}'.");
            }

            Player3DManifest manifest =
                JsonUtility.FromJson<Player3DManifest>(text.text);
            if (manifest == null || manifest.parts == null)
            {
                throw new InvalidOperationException(
                    "Player 3D manifest is malformed or has no parts.");
            }

            if (!manifest.runtime_integrated)
            {
                throw new InvalidOperationException(
                    "The Player 3D manifest is not marked as runtime integrated.");
            }

            if (!string.Equals(
                    manifest.pose,
                    "apose",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The production Player 3D model must use the A-pose as " +
                    $"its bind pose; manifest reports '{manifest.pose}'.");
            }

            if (Mathf.Abs(manifest.height_m - ExpectedHeight) > 0.001f)
            {
                throw new InvalidOperationException(
                    $"Player 3D manifest height is {manifest.height_m:F3} m; " +
                    $"expected {ExpectedHeight:F3} m.");
            }

            if (manifest.mesh_count != manifest.parts.Length)
            {
                throw new InvalidOperationException(
                    $"Manifest declares {manifest.mesh_count} meshes but " +
                    $"contains {manifest.parts.Length} part records.");
            }

            if (manifest.triangle_count <= 0 ||
                manifest.triangle_count > MaximumTriangleCount)
            {
                throw new InvalidOperationException(
                    $"Player 3D triangle count {manifest.triangle_count} is " +
                    $"outside the supported range 1..{MaximumTriangleCount}.");
            }

            if (manifest.actions == null ||
                manifest.action_count != manifest.actions.Length ||
                manifest.action_count == 0)
            {
                throw new InvalidOperationException(
                    $"Manifest declares {manifest.action_count} actions but " +
                    $"contains {manifest.actions?.Length ?? 0} records.");
            }

            if (!string.Equals(manifest.forward_axis, "-Y", StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.anatomical_left_axis,
                    "+X",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Player 3D source coordinate contract must be forward -Y " +
                    "and anatomical left +X.");
            }

            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.parts.Length; index++)
            {
                Player3DManifestPart part = manifest.parts[index];
                if (part == null || string.IsNullOrWhiteSpace(part.name))
                {
                    throw new InvalidOperationException(
                        $"Manifest part {index} has no name.");
                }

                if (!names.Add(part.name))
                {
                    throw new InvalidOperationException(
                        $"Manifest contains duplicate part '{part.name}'.");
                }

                if (!PaletteHex.ContainsKey(part.material))
                {
                    throw new InvalidOperationException(
                        $"Part '{part.name}' uses unknown palette material " +
                        $"'{part.material}'.");
                }
            }

            foreach (string requiredName in RequiredBodyParts.Keys)
            {
                if (!names.Contains(requiredName))
                {
                    throw new InvalidOperationException(
                        $"Manifest is missing anatomical mesh '{requiredName}'.");
                }
            }

            HashSet<string> actionNames =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.actions.Length; index++)
            {
                Player3DManifestAction action = manifest.actions[index];
                if (action == null ||
                    string.IsNullOrWhiteSpace(action.name) ||
                    string.IsNullOrWhiteSpace(action.category) ||
                    action.duration_seconds <= 0f)
                {
                    throw new InvalidOperationException(
                        $"Manifest action {index} is incomplete.");
                }

                if (!actionNames.Add(action.name))
                {
                    throw new InvalidOperationException(
                        $"Manifest contains duplicate action '{action.name}'.");
                }
            }

            return manifest;
        }

        private static void ImportAnimationSources()
        {
            if (!Directory.Exists(AnimationFolderPath))
            {
                return;
            }

            string[] paths = Directory.GetFiles(
                AnimationFolderPath,
                "*.fbx",
                SearchOption.AllDirectories);
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < paths.Length; index++)
            {
                AssetDatabase.ImportAsset(
                    paths[index].Replace('\\', '/'),
                    ImportAssetOptions.ForceUpdate);
            }
        }

        private static Material EnsureSharedMaterial()
        {
            // The project's own Lit: stock URP Lit with a vertex snap
            // wrapped around three of its passes. Regenerating this
            // material against the package shader instead would silently
            // take the hero back off the PS1 jitter.
            Shader shader = Shader.Find("Bar Promenade/PS1 Lit");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "PS1 Lit shader is unavailable; cannot create Player " +
                    "3D material.");
            }

            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "Player3DLit"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.color = Color.white;
            SetColorIfPresent(material, "_BaseColor", Color.white);
            SetFloatIfPresent(material, "_Metallic", 0f);
            SetFloatIfPresent(material, "_Smoothness", 0f);
            SetFloatIfPresent(material, "_SpecularHighlights", 0f);
            SetFloatIfPresent(material, "_EnvironmentReflections", 0f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Player3DAnimationBinding[] LoadAnimationBindings(
            Player3DManifest manifest)
        {
            if (!Directory.Exists(AnimationFolderPath))
            {
                throw new InvalidOperationException(
                    $"Player3D animation folder is missing at " +
                    $"'{AnimationFolderPath}'.");
            }

            Dictionary<string, AnimationClip> clips =
                new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            string[] paths = Directory.GetFiles(
                AnimationFolderPath,
                "*.fbx",
                SearchOption.AllDirectories);
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            for (int pathIndex = 0; pathIndex < paths.Length; pathIndex++)
            {
                string assetPath = paths[pathIndex].Replace('\\', '/');
                UnityEngine.Object[] assets =
                    AssetDatabase.LoadAllAssetsAtPath(assetPath);
                for (int assetIndex = 0;
                     assetIndex < assets.Length;
                     assetIndex++)
                {
                    if (!(assets[assetIndex] is AnimationClip clip) ||
                        clip.name.StartsWith(
                            "__preview__",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string clipName = NormalizeClipName(clip.name);
                    if (clips.ContainsKey(clipName))
                    {
                        throw new InvalidOperationException(
                            $"Animation sources contain duplicate clip " +
                            $"'{clipName}'.");
                    }

                    clips.Add(clipName, clip);
                }
            }

            if (clips.Count != manifest.action_count)
            {
                throw new InvalidOperationException(
                    $"Unity imported {clips.Count} Player 3D animation clips; " +
                    $"manifest declares {manifest.action_count}.");
            }

            Player3DAnimationBinding[] bindings =
                new Player3DAnimationBinding[manifest.actions.Length];
            for (int index = 0; index < manifest.actions.Length; index++)
            {
                Player3DManifestAction action = manifest.actions[index];
                if (!clips.TryGetValue(
                        action.name,
                        out AnimationClip clip))
                {
                    throw new InvalidOperationException(
                        $"Unity import is missing Player 3D clip " +
                        $"'{action.name}'.");
                }

                if (clip.name != action.name)
                {
                    throw new InvalidOperationException(
                        $"Imported clip '{clip.name}' retained an FBX stack " +
                        $"prefix; expected '{action.name}'.");
                }

                if (clip.isLooping != action.loop)
                {
                    throw new InvalidOperationException(
                        $"Imported clip '{action.name}' loop flag differs " +
                        "from the manifest.");
                }

                if (Mathf.Abs(clip.length - action.duration_seconds) >
                    1f / 24f)
                {
                    throw new InvalidOperationException(
                        $"Imported clip '{action.name}' lasts " +
                        $"{clip.length:F3} s; manifest declares " +
                        $"{action.duration_seconds:F3} s.");
                }

                bindings[index] = new Player3DAnimationBinding(
                    action.name,
                    action.category,
                    clip,
                    action.duration_seconds,
                    action.loop);
            }

            return bindings;
        }

        private static void BuildPrefab(
            GameObject modelAsset,
            Player3DManifest manifest,
            Material sharedMaterial,
            Player3DAnimationBinding[] animations)
        {
            GameObject prefabRoot = new GameObject("Player3D");
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate imported Player 3D model.");
                }

                model.name = "Model";
                model.transform.SetParent(prefabRoot.transform, false);
                model.transform.localPosition = Vector3.zero;
                // Blender authors the character facing -Y. After the FBX
                // axis conversion that anatomical front is Unity-local -Z,
                // so adapt the imported model once at the prefab boundary.
                // The gameplay/prefab contract remains conventional +Z.
                model.transform.localRotation =
                    Quaternion.Euler(0f, 180f, 0f);
                model.transform.localScale = Vector3.one;

                Dictionary<string, Renderer> renderersByName =
                    IndexByName(
                        model.GetComponentsInChildren<Renderer>(true),
                        renderer => renderer.name,
                        "renderer");
                Dictionary<string, Transform> transformsByName =
                    IndexByName(
                        model.GetComponentsInChildren<Transform>(true),
                        item => item.name,
                        "transform");

                if (renderersByName.Count != manifest.mesh_count)
                {
                    throw new InvalidOperationException(
                        $"Imported model exposes {renderersByName.Count} " +
                        $"renderers; manifest declares {manifest.mesh_count}.");
                }

                List<Player3DMeshBinding> meshBindings =
                    new List<Player3DMeshBinding>(manifest.parts.Length);
                List<Player3DAnatomicalPartBinding> anatomicalBindings =
                    new List<Player3DAnatomicalPartBinding>(
                        RequiredBodyParts.Count);

                for (int index = 0; index < manifest.parts.Length; index++)
                {
                    Player3DManifestPart source = manifest.parts[index];
                    if (!renderersByName.TryGetValue(
                            source.name,
                            out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            $"Imported model is missing renderer " +
                            $"'{source.name}'.");
                    }

                    if (!transformsByName.TryGetValue(
                            source.bone,
                            out Transform bone))
                    {
                        throw new InvalidOperationException(
                            $"Part '{source.name}' references missing bone " +
                            $"'{source.bone}'.");
                    }

                    renderer.sharedMaterials = Enumerable
                        .Repeat(
                            sharedMaterial,
                            Math.Max(1, renderer.sharedMaterials.Length))
                        .ToArray();
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.motionVectorGenerationMode =
                        MotionVectorGenerationMode.Object;

                    Color color = ParsePaletteColor(source.material);
                    Player3DMeshBinding meshBinding =
                        new Player3DMeshBinding(
                            source.name,
                            source.role,
                            source.bone,
                            source.sprite_part,
                            source.side,
                            source.material,
                            renderer,
                            bone,
                            color);
                    meshBindings.Add(meshBinding);

                    if (RequiredBodyParts.TryGetValue(
                            source.name,
                            out Player3DAnatomicalPart anatomicalPart))
                    {
                        anatomicalBindings.Add(
                            new Player3DAnatomicalPartBinding(
                                anatomicalPart,
                                renderer,
                                bone));
                    }
                }

                anatomicalBindings.Sort(
                    (left, right) => left.Part.CompareTo(right.Part));

                Animator animator = model.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    animator = model.AddComponent<Animator>();
                    animator.avatar = FindModelAvatar();
                }

                if (animator.avatar == null)
                {
                    animator.avatar = FindModelAvatar();
                }

                if (animator.avatar == null || !animator.avatar.isValid)
                {
                    throw new InvalidOperationException(
                        "Imported Player 3D model has no valid Generic Avatar.");
                }

                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                Transform head = RequireTransform(transformsByName, "head");
                Transform chest = RequireTransform(transformsByName, "chest");
                Transform pelvis = RequireTransform(transformsByName, "pelvis");
                Transform leftFoot = RequireTransform(transformsByName, "foot.L");
                Transform rightFoot = RequireTransform(transformsByName, "foot.R");
                Transform leftGrip = FindOptionalTransform(
                    transformsByName,
                    "SOCKET_Grip.L",
                    "hand.L");
                Transform rightGrip = FindOptionalTransform(
                    transformsByName,
                    "SOCKET_Grip.R",
                    "hand.R");
                Transform rightCigarette = RequireTransform(
                    transformsByName,
                    "SOCKET_Cigarette.R");
                Transform mouth = FindOptionalTransform(
                    transformsByName,
                    "SOCKET_Mouth",
                    "head");

                Renderer[] rendererArray = meshBindings
                    .Select(binding => binding.Renderer)
                    .ToArray();
                Bounds localBounds = CalculateLocalBounds(
                    prefabRoot.transform,
                    rendererArray);
                ValidateImportedGeometry(localBounds, manifest, rendererArray);

                Player3DAssetRegistry registry =
                    prefabRoot.AddComponent<Player3DAssetRegistry>();
                registry.Configure(
                    animator,
                    model.transform,
                    rendererArray,
                    meshBindings.ToArray(),
                    anatomicalBindings.ToArray(),
                    animations,
                    new Player3DBoneAnchors(
                        head,
                        chest,
                        pelvis,
                        leftFoot,
                        rightFoot,
                        leftGrip,
                        rightGrip,
                        rightCigarette,
                        mouth),
                    new Player3DMetrics(
                        manifest.height_m,
                        localBounds,
                        Vector3.forward),
                    manifest.generator_version,
                    manifest.pose,
                    manifest.triangle_count,
                    CalculateBuildSignature());
                registry.ApplyPalette();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    prefabRoot,
                    PrefabPath,
                    out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save Player 3D prefab at '{PrefabPath}'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        private static void ValidateImportedGeometry(
            Bounds localBounds,
            Player3DManifest manifest,
            IReadOnlyList<Renderer> renderers)
        {
            float heightTolerance = 0.035f;
            if (Mathf.Abs(localBounds.size.y - manifest.height_m) >
                heightTolerance)
            {
                throw new InvalidOperationException(
                    $"Imported Player 3D height is {localBounds.size.y:F3} m; " +
                    $"manifest declares {manifest.height_m:F3} m.");
            }

            if (Mathf.Abs(localBounds.min.y) > 0.025f)
            {
                throw new InvalidOperationException(
                    $"Imported Player 3D feet are at local Y " +
                    $"{localBounds.min.y:F3}, expected ground Y=0.");
            }

            int triangleCount = 0;
            for (int index = 0; index < renderers.Count; index++)
            {
                Mesh mesh = GetRendererMesh(renderers[index]);
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderers[index].name}' has no mesh.");
                }

                for (int subMeshIndex = 0;
                     subMeshIndex < mesh.subMeshCount;
                     subMeshIndex++)
                {
                    triangleCount +=
                        (int)(mesh.GetIndexCount(subMeshIndex) / 3);
                }
            }

            if (triangleCount != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    $"Imported meshes contain {triangleCount} triangles; " +
                    $"manifest declares {manifest.triangle_count}.");
            }
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

        private static string CalculateBuildSignature()
        {
            var inputs = new List<string>
            {
                BuildSchemaVersion.ToString(),
                DependencyStamp(ModelPath),
                DependencyStamp(ManifestPath),
                DependencyStamp(SetupScriptPath),
                DependencyStamp(ImporterScriptPath),
                DependencyStamp(RegistryScriptPath)
            };

            string[] animationPaths = Directory.Exists(AnimationFolderPath)
                ? Directory.GetFiles(
                    AnimationFolderPath,
                    "*.fbx",
                    SearchOption.AllDirectories)
                : Array.Empty<string>();
            Array.Sort(animationPaths, StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < animationPaths.Length; index++)
            {
                string assetPath = animationPaths[index].Replace('\\', '/');
                inputs.Add(assetPath);
                inputs.Add(DependencyStamp(assetPath));
            }

            return Hash128.Compute(string.Join("|", inputs)).ToString();
        }

        private static string DependencyStamp(string assetPath)
        {
            return AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
        }

        private static Avatar FindModelAvatar()
        {
            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            for (int index = 0; index < assets.Length; index++)
            {
                if (assets[index] is Avatar avatar)
                {
                    return avatar;
                }
            }

            return null;
        }

        private static Bounds CalculateLocalBounds(
            Transform root,
            IReadOnlyList<Renderer> renderers)
        {
            if (renderers.Count == 0)
            {
                throw new InvalidOperationException(
                    "Player 3D model contains no renderers.");
            }

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

                Bounds meshBounds = mesh.bounds;
                Vector3 min = meshBounds.min;
                Vector3 max = meshBounds.max;
                Matrix4x4 rendererToRoot =
                    root.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 rendererLocalPoint = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    Vector3 rootLocalPoint =
                        rendererToRoot.MultiplyPoint3x4(rendererLocalPoint);
                    if (!initialized)
                    {
                        result = new Bounds(rootLocalPoint, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        result.Encapsulate(rootLocalPoint);
                    }
                }
            }

            return result;
        }

        private static Dictionary<string, T> IndexByName<T>(
            IEnumerable<T> items,
            Func<T, string> getName,
            string itemLabel)
        {
            Dictionary<string, T> result =
                new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (T item in items)
            {
                string name = getName(item);
                if (result.ContainsKey(name))
                {
                    throw new InvalidOperationException(
                        $"Imported Player 3D hierarchy contains duplicate " +
                        $"{itemLabel} name '{name}'.");
                }

                result.Add(name, item);
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
                $"Imported Player 3D hierarchy is missing bone '{name}'.");
        }

        private static Transform FindOptionalTransform(
            IReadOnlyDictionary<string, Transform> transforms,
            string preferredName,
            string fallbackName)
        {
            return transforms.TryGetValue(preferredName, out Transform preferred)
                ? preferred
                : RequireTransform(transforms, fallbackName);
        }

        private static Color ParsePaletteColor(string materialName)
        {
            string hex = PaletteHex[materialName];
            if (!ColorUtility.TryParseHtmlString($"#{hex}", out Color color))
            {
                throw new InvalidOperationException(
                    $"Invalid Player 3D palette color '{hex}'.");
            }

            return color;
        }

        private static string NormalizeClipName(string sourceName)
        {
            int separator = sourceName.LastIndexOf('|');
            return separator >= 0 && separator + 1 < sourceName.Length
                ? sourceName.Substring(separator + 1)
                : sourceName;
        }

        private static void SetColorIfPresent(
            Material material,
            string propertyName,
            Color value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, value);
            }
        }

        private static void SetFloatIfPresent(
            Material material,
            string propertyName,
            float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
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
        private sealed class Player3DManifest
        {
            public string generator_version;
            public bool runtime_integrated;
            public float height_m;
            public string pose;
            public string forward_axis;
            public string anatomical_left_axis;
            public int mesh_count;
            public int triangle_count;
            public int action_count;
            public Player3DManifestPart[] parts;
            public Player3DManifestAction[] actions;
        }

        [Serializable]
        private sealed class Player3DManifestPart
        {
            public string name;
            public string role;
            public string bone;
            public string sprite_part;
            public string side;
            public string material;
        }

        [Serializable]
        private sealed class Player3DManifestAction
        {
            public string name;
            public string category;
            public float duration_seconds;
            public bool loop;
        }
    }
}
