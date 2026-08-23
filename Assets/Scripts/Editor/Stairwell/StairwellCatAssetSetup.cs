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
    /// Imports the Cheshire stairwell cat FBX and builds the passive
    /// runtime prefab outside Resources, binding it to the one
    /// addressable provider asset. The cashier setup shape minus all
    /// avatar work: the cat has no armature, only pivot empties.
    /// </summary>
    [InitializeOnLoad]
    public static class StairwellCatAssetSetup
    {
        public const string ModelPath =
            "Assets/Stairwell/Cat/Models/StairwellCat3D.fbx";
        public const string ManifestPath =
            "Assets/Stairwell/Cat/Models/StairwellCat3D.json";
        public const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        public const string GrinMaterialPath =
            "Assets/Resources/Materials/StairwellCatGrin.mat";
        public const string PrefabPath =
            "Assets/Stairwell/Cat/Prefabs/StairwellCat.prefab";
        public const string ProviderPath =
            "Assets/Resources/Stairwell/StairwellCatProvider.asset";

        private const string ExpectedDesignId =
            "cheshire_stairwell_cat_v1";
        private const string ExpectedGrinUvArc = "arclength_u_v1";
        private const string BodyRendererName = "GEO_Haunches";
        private const int MinimumTriangleCount = 400;
        private const int MaximumTriangleCount = 1600;

        private static readonly string[] ExpectedPivotNames =
        {
            StairwellCatRigAnchors.ChestPivotName,
            StairwellCatRigAnchors.HeadPivotName,
            StairwellCatRigAnchors.EarLeftPivotName,
            StairwellCatRigAnchors.EarRightPivotName,
            StairwellCatRigAnchors.TailPivotNames[0],
            StairwellCatRigAnchors.TailPivotNames[1],
            StairwellCatRigAnchors.TailPivotNames[2]
        };

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static StairwellCatAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/Stairwell Cat 3D/Build Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log(
                $"Stairwell cat prefab rebuilt at '{PrefabPath}'.");
        }

        [MenuItem("Bar Promenade/Stairwell Cat 3D/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log(
                "Stairwell cat passive prefab contract is valid.");
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) &&
                File.Exists(ManifestPath) &&
                File.Exists(SharedMaterialPath) &&
                File.Exists(GrinMaterialPath);
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
                    "Stairwell cat build requires its FBX/manifest, " +
                    "the shared Player3DLit material and the grin " +
                    "material.");
            }

            isBuilding = true;
            try
            {
                EnsureFolderForAsset(PrefabPath);
                EnsureFolderForAsset(ProviderPath);
                AssetDatabase.ImportAsset(
                    ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                StairwellCatManifest manifest =
                    LoadAndValidateManifest();
                GameObject modelAsset =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        ModelPath);
                if (modelAsset == null)
                {
                    throw new InvalidOperationException(
                        $"Unity did not import a model from " +
                        $"'{ModelPath}'.");
                }

                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialPath);
                Material grinMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        GrinMaterialPath);
                if (sharedMaterial == null || grinMaterial == null)
                {
                    throw new InvalidOperationException(
                        "The shared Player3DLit or the grin material " +
                        "failed to load.");
                }

                BuildPrefab(
                    modelAsset,
                    sharedMaterial,
                    grinMaterial,
                    manifest);
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
            StairwellCatManifest manifest = LoadAndValidateManifest();

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Stairwell cat prefab is missing at " +
                    $"'{PrefabPath}'.");
            }

            StairwellCatRigAnchors anchors =
                prefab.GetComponent<StairwellCatRigAnchors>();
            if (anchors == null || !anchors.IsBound)
            {
                throw new InvalidOperationException(
                    "Stairwell cat prefab has no fully bound rig " +
                    "anchors.");
            }

            if (anchors.Renderers.Count != manifest.mesh_count ||
                anchors.RendererBindings.Count != manifest.mesh_count)
            {
                throw new InvalidOperationException(
                    "Stairwell cat renderer counts differ from the " +
                    "deterministic manifest.");
            }

            if (!string.Equals(
                    anchors.DesignId,
                    manifest.design_id,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    anchors.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal) ||
                anchors.SourceTriangleCount != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    "Stairwell cat rig anchor source metadata is " +
                    "stale.");
            }

            if (Mathf.Abs(
                    anchors.LocalBounds.max.y -
                    manifest.sitting_height_m) > 0.035f ||
                anchors.LocalBounds.min.y < -0.35f)
            {
                throw new InvalidOperationException(
                    "Stairwell cat prefab bounds lost the authored " +
                    "sitting height or tail reach.");
            }

            if (prefab.GetComponentsInChildren<Collider>(true)
                    .Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true)
                    .Length != 0 ||
                prefab.GetComponentsInChildren<AudioSource>(true)
                    .Length != 0 ||
                prefab.GetComponentsInChildren<Camera>(true)
                    .Length != 0)
            {
                throw new InvalidOperationException(
                    "The passive cat prefab must contain no " +
                    "Collider, Light, Rigidbody, AudioSource or " +
                    "Camera component.");
            }

            Material sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedMaterialPath);
            Material grinMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    GrinMaterialPath);
            for (int index = 0;
                 index < anchors.Renderers.Count;
                 index++)
            {
                Renderer renderer = anchors.Renderers[index];
                if (renderer == null ||
                    renderer.sharedMaterials.Length != 1)
                {
                    throw new InvalidOperationException(
                        "Every cat renderer must carry exactly one " +
                        "material.");
                }

                bool isGrin = renderer == anchors.GrinRenderer;
                Material expected = isGrin
                    ? grinMaterial
                    : sharedMaterial;
                if (renderer.sharedMaterial != expected)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.name}' does not " +
                        "reference its designated material.");
                }

                if (renderer.shadowCastingMode !=
                    ShadowCastingMode.Off)
                {
                    throw new InvalidOperationException(
                        "Cat renderers must not cast shadows, " +
                        "matching the retired sprite contract.");
                }
            }

            if (anchors.GrinRenderer.enabled)
            {
                throw new InvalidOperationException(
                    "The grin renderer must ship disabled: by " +
                    "default the grin does not exist.");
            }

            StairwellCatProvider provider =
                AssetDatabase.LoadAssetAtPath<StairwellCatProvider>(
                    ProviderPath);
            if (provider == null || provider.CatPrefab != prefab)
            {
                throw new InvalidOperationException(
                    "The cat provider asset must reference the " +
                    "built prefab.");
            }
        }

        private static void ValidateDependencyStamp()
        {
            if (!SourcesExist())
            {
                return;
            }

            StairwellCatManifest manifest;
            try
            {
                manifest = LoadAndValidateManifest();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not validate stairwell cat source " +
                    $"manifest: {exception}");
                return;
            }

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            StairwellCatRigAnchors anchors = prefab != null
                ? prefab.GetComponent<StairwellCatRigAnchors>()
                : null;
            if (anchors == null ||
                !string.Equals(
                    anchors.BuildSignature,
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
                    $"Could not build stairwell cat prefab: " +
                    $"{exception}");
            }
        }

        private static StairwellCatManifest LoadAndValidateManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import manifest '{ManifestPath}'.");
            }

            StairwellCatManifest manifest =
                JsonUtility.FromJson<StairwellCatManifest>(
                    source.text);
            if (manifest == null ||
                manifest.parts == null ||
                manifest.pivot_names == null ||
                manifest.anchor_names == null)
            {
                throw new InvalidOperationException(
                    "Stairwell cat manifest is malformed.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    ExpectedDesignId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.grin_uv_arc,
                    ExpectedGrinUvArc,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Stairwell cat design or grin UV contract " +
                    "differs from the approved source.");
            }

            if (manifest.mesh_count != manifest.parts.Length ||
                manifest.mesh_count < 10 ||
                manifest.mesh_count > 24 ||
                manifest.triangle_count < MinimumTriangleCount ||
                manifest.triangle_count > MaximumTriangleCount)
            {
                throw new InvalidOperationException(
                    "Stairwell cat manifest mesh or triangle budget " +
                    "is invalid.");
            }

            if (manifest.grin_width_m <= manifest.head_width_m)
            {
                throw new InvalidOperationException(
                    "The grin must stay wider than the head - that " +
                    "is the joke.");
            }

            if (manifest.emissive ||
                manifest.colliders ||
                manifest.lights ||
                manifest.rigidbodies ||
                manifest.animation_count != 0 ||
                !string.Equals(
                    manifest.material_asset,
                    SharedMaterialPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.grin_material_asset,
                    GrinMaterialPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Stairwell cat must be collider/light/Rigidbody-" +
                    "free, animation-free and reuse the designated " +
                    "materials.");
            }

            if (!manifest.pivot_names.SequenceEqual(
                    ExpectedPivotNames,
                    StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Stairwell cat pivots diverge from the authored " +
                    "articulation set.");
            }

            if (string.IsNullOrWhiteSpace(manifest.build_signature) ||
                manifest.build_signature.Length != 64)
            {
                throw new InvalidOperationException(
                    "Stairwell cat manifest lacks deterministic " +
                    "source metadata.");
            }

            ValidateManifestDesignParts(manifest.parts);
            return manifest;
        }

        private static void ValidateManifestDesignParts(
            IReadOnlyList<StairwellCatManifestPart> parts)
        {
            Dictionary<string, StairwellCatManifestPart> byName =
                parts.ToDictionary(
                    part => part.name,
                    StringComparer.Ordinal);
            RequirePart(byName, "GEO_Haunches", "cat_body", "");
            RequirePart(
                byName,
                "GEO_Torso",
                "cat_chest",
                StairwellCatRigAnchors.ChestPivotName);
            RequirePart(
                byName,
                "GEO_Head",
                "cat_head",
                StairwellCatRigAnchors.HeadPivotName);
            RequirePart(
                byName,
                "ACC_Grin",
                "cheshire_grin",
                StairwellCatRigAnchors.HeadPivotName);
            RequirePart(
                byName,
                "GEO_Ear.L",
                "cat_ear",
                StairwellCatRigAnchors.EarLeftPivotName);
            RequirePart(
                byName,
                "GEO_Ear.R",
                "cat_ear",
                StairwellCatRigAnchors.EarRightPivotName);
            for (int index = 0;
                 index < StairwellCatRigAnchors.TailPivotCount;
                 index++)
            {
                RequirePart(
                    byName,
                    $"TAIL_Segment.{index + 1:00}",
                    "cat_tail",
                    StairwellCatRigAnchors.TailPivotNames[index]);
            }
        }

        private static void RequirePart(
            IReadOnlyDictionary<string, StairwellCatManifestPart>
                parts,
            string name,
            string role,
            string pivot)
        {
            if (!parts.TryGetValue(
                    name,
                    out StairwellCatManifestPart part) ||
                !string.Equals(
                    part.role,
                    role,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    part.pivot,
                    pivot,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Stairwell cat manifest lost required part " +
                    $"'{name}' with role '{role}' on pivot " +
                    $"'{pivot}'.");
            }
        }

        private static void BuildPrefab(
            GameObject modelAsset,
            Material sharedMaterial,
            Material grinMaterial,
            StairwellCatManifest manifest)
        {
            GameObject prefabRoot = new GameObject("StairwellCat");
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(modelAsset)
                        as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate the imported cat " +
                        "model.");
                }

                model.name = "Model";
                model.transform.SetParent(prefabRoot.transform, false);
                model.transform.localPosition = Vector3.zero;
                // Blender -Y forward arrives facing Unity -Z; the
                // half turn makes the prefab face +Z like every
                // authored character, so factories aim it with plain
                // rotations.
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
                        "Imported cat renderer count differs from " +
                        "the manifest.");
                }

                List<Renderer> rendererList =
                    new List<Renderer>(manifest.parts.Length);
                List<StairwellCatRendererBinding> bindings =
                    new List<StairwellCatRendererBinding>(
                        manifest.parts.Length);
                Renderer grinRenderer = null;
                Renderer bodyRenderer = null;
                for (int index = 0;
                     index < manifest.parts.Length;
                     index++)
                {
                    StairwellCatManifestPart source =
                        manifest.parts[index];
                    if (!renderersByName.TryGetValue(
                            source.name,
                            out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            "Imported cat is missing renderer " +
                            $"'{source.name}'.");
                    }

                    bool isGrin = string.Equals(
                        source.name,
                        StairwellCatRigAnchors.GrinRendererName,
                        StringComparison.Ordinal);
                    renderer.sharedMaterials = new[]
                    {
                        isGrin ? grinMaterial : sharedMaterial
                    };
                    renderer.shadowCastingMode =
                        ShadowCastingMode.Off;
                    renderer.receiveShadows = !isGrin;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage =
                        ReflectionProbeUsage.Off;
                    renderer.motionVectorGenerationMode =
                        MotionVectorGenerationMode.ForceNoMotion;
                    if (isGrin)
                    {
                        grinRenderer = renderer;
                        renderer.enabled = false;
                    }

                    if (string.Equals(
                            source.name,
                            BodyRendererName,
                            StringComparison.Ordinal))
                    {
                        bodyRenderer = renderer;
                    }

                    bindings.Add(
                        new StairwellCatRendererBinding(
                            source.name,
                            source.pivot,
                            source.role,
                            source.palette_name,
                            renderer,
                            ParseColor(source.base_color)));
                    rendererList.Add(renderer);
                }

                if (grinRenderer == null || bodyRenderer == null)
                {
                    throw new InvalidOperationException(
                        "Imported cat lost its grin or haunches " +
                        "renderer.");
                }

                Transform[] tailPivots =
                    new Transform[
                        StairwellCatRigAnchors.TailPivotCount];
                for (int index = 0;
                     index < tailPivots.Length;
                     index++)
                {
                    tailPivots[index] = RequireTransform(
                        transformsByName,
                        StairwellCatRigAnchors.TailPivotNames[index]);
                }

                Renderer[] renderers = rendererList.ToArray();
                StairwellCatRigAnchors anchors =
                    prefabRoot.AddComponent<StairwellCatRigAnchors>();
                anchors.Configure(
                    model.transform,
                    renderers,
                    bindings.ToArray(),
                    RequireTransform(
                        transformsByName,
                        StairwellCatRigAnchors.ChestPivotName),
                    RequireTransform(
                        transformsByName,
                        StairwellCatRigAnchors.HeadPivotName),
                    RequireTransform(
                        transformsByName,
                        StairwellCatRigAnchors.EarLeftPivotName),
                    RequireTransform(
                        transformsByName,
                        StairwellCatRigAnchors.EarRightPivotName),
                    tailPivots,
                    RequireTransform(
                        transformsByName,
                        StairwellCatRigAnchors.MuzzleAnchorName),
                    grinRenderer,
                    bodyRenderer,
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
                        "Could not save stairwell cat prefab at " +
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
            StairwellCatProvider provider =
                AssetDatabase.LoadAssetAtPath<StairwellCatProvider>(
                    ProviderPath);
            if (provider == null)
            {
                provider = ScriptableObject
                    .CreateInstance<StairwellCatProvider>();
                AssetDatabase.CreateAsset(provider, ProviderPath);
            }

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            SerializedObject serialized =
                new SerializedObject(provider);
            serialized.FindProperty("catPrefab")
                .objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(provider);
        }

        private static Dictionary<string, Transform>
            IndexUniqueTransforms(GameObject root)
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
                        "Imported cat hierarchy contains duplicate " +
                        $"transform name '{transform.name}'.");
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
                        "Imported cat hierarchy contains duplicate " +
                        $"renderer name '{renderer.name}'.");
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
                "Imported cat hierarchy is missing transform " +
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
                MeshFilter filter =
                    renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
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
                    "Stairwell cat model contains no renderers.");
            }

            return result;
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
        private sealed class StairwellCatManifest
        {
            public string generator_version;
            public string design_id;
            public string display_name;
            public int seed;
            public float sitting_height_m;
            public string forward_axis;
            public int mesh_count;
            public int triangle_count;
            public int[] triangle_budget;
            public string[] pivot_names;
            public string[] anchor_names;
            public float[] bounds_min;
            public float[] bounds_max;
            public string material_asset;
            public string grin_material_asset;
            public float grin_width_m;
            public float head_width_m;
            public int grin_tooth_count;
            public string grin_uv_arc;
            public bool emissive;
            public bool colliders;
            public bool lights;
            public bool rigidbodies;
            public int animation_count;
            public string build_signature;
            public StairwellCatManifestPart[] parts;
        }

        [Serializable]
        private sealed class StairwellCatManifestPart
        {
            public string name;
            public string pivot;
            public string role;
            public string palette_name;
            public float[] base_color;
            public int vertices;
            public int triangles;
        }
    }
}
