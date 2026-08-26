using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Turns the imported bar model into a runtime prefab and refuses to
    /// let it carry anything a prefab from Blender must not carry.
    ///
    /// This is where the pipeline is actually defended - not in the
    /// tests. The tests confirm the room agrees with the layout plan;
    /// this class is what stops a collider, a light or a camera from
    /// riding in on an FBX in the first place, because those belong to
    /// `BarInteriorLayoutPlan` and to nothing else.
    /// </summary>
    [InitializeOnLoad]
    public static class BarAssetSetup
    {
        public const string InteriorModelPath =
            "Assets/Bar/Models/BarInterior3D.fbx";
        public const string ManifestPath =
            "Assets/Bar/Models/Bar3D.json";
        public const string InteriorPrefabPath =
            "Assets/Resources/Bar/BarInterior3D.prefab";
        public const string FacadeModelPath =
            "Assets/Bar/Models/BarFacade3D.fbx";
        public const string FacadeManifestPath =
            "Assets/Bar/Models/BarFacade3D.json";
        public const string FacadePrefabPath =
            "Assets/Resources/Bar/BarFacade3D.prefab";
        public const string SharedLitMaterialPath =
            "Assets/Resources/Materials/RuntimePrimitiveLit.mat";
        public const string SharedEmissionMaterialPath =
            "Assets/Resources/Materials/CityNoirEmission.mat";

        private const string ExpectedDesignId = "bar_interior_v2";
        private const string ExpectedFacadeDesignId = "bar_facade_v1";
        private const int MaximumTriangles = 24000;
        private const int MaximumRenderers = 200;

        //  Mirrors `BarInteriorLayoutPlanner`. Duplicated deliberately:
        //  the point of the check is that the model and the planner
        //  agree, and a shared constant would make the check vacuous.
        private const float ExpectedWidth = 22f;
        private const float ExpectedDepth = 16f;
        private const float ExpectedHeight = 4.8f;
        private const float ExpectedWallThickness = 0.3f;
        private const float ExpectedDoorWidth = 3.2f;

        private static bool buildQueued;

        public static bool IsBuilding { get; private set; }

        static BarAssetSetup()
        {
            QueueBuildWhenSourcesExist();
        }

        [MenuItem("Bar Promenade/Bar/Build Runtime Prefabs")]
        public static void Run()
        {
            BuildOrThrow();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Bar Promenade/Bar/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("Bar model contract is valid.");
        }

        public static void RunBatch()
        {
            try
            {
                BuildOrThrow();
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError(error);
                EditorApplication.Exit(1);
            }
        }

        public static bool IsModelPath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                (string.Equals(
                     path,
                     InteriorModelPath,
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     path,
                     FacadeModelPath,
                     StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsManifestPath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                (string.Equals(
                     path,
                     ManifestPath,
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     path,
                     FacadeManifestPath,
                     StringComparison.OrdinalIgnoreCase));
        }

        public static bool SourcesExist()
        {
            return File.Exists(InteriorModelPath) &&
                File.Exists(ManifestPath) &&
                File.Exists(FacadeModelPath) &&
                File.Exists(FacadeManifestPath);
        }

        public static void QueueBuildWhenSourcesExist()
        {
            if (buildQueued || !SourcesExist())
            {
                return;
            }

            buildQueued = true;
            EditorApplication.delayCall += RunQueuedBuild;
        }

        public static void BuildOrThrow()
        {
            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "Bar model sources are missing. Run " +
                    "tools/build-bar-3d-model.py through Blender first.");
            }

            IsBuilding = true;
            try
            {
                foreach (string path in new[]
                         {
                             InteriorModelPath, ManifestPath,
                             FacadeModelPath, FacadeManifestPath,
                         })
                {
                    AssetDatabase.ImportAsset(
                        path,
                        ImportAssetOptions.ForceUpdate |
                        ImportAssetOptions.ForceSynchronousImport);
                }

                EnsureFolderForAsset(InteriorPrefabPath);
                BuildPrefab(
                    LoadAndValidateManifest(ManifestPath, ExpectedDesignId),
                    InteriorModelPath,
                    InteriorPrefabPath,
                    "BarInterior3D");
                BuildPrefab(
                    LoadAndValidateManifest(
                        FacadeManifestPath, ExpectedFacadeDesignId),
                    FacadeModelPath,
                    FacadePrefabPath,
                    "BarFacade3D");
            }
            finally
            {
                IsBuilding = false;
            }

            AssetDatabase.Refresh();
            ValidateOrThrow();
        }

        public static void ValidateOrThrow()
        {
            ValidatePrefabOrThrow(
                LoadAndValidateManifest(ManifestPath, ExpectedDesignId),
                InteriorPrefabPath,
                true);
            ValidatePrefabOrThrow(
                LoadAndValidateManifest(
                    FacadeManifestPath, ExpectedFacadeDesignId),
                FacadePrefabPath,
                false);
        }

        private static void ValidatePrefabOrThrow(
            BarManifest manifest,
            string prefabPath,
            bool checkRoomDimensions)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"A bar prefab is missing at '{prefabPath}'.");
            }

            var problems = new List<string>();

            BarAssetRegistry registry =
                prefab.GetComponent<BarAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "Bar interior prefab has no BarAssetRegistry.");
            }

            if (!string.Equals(
                    registry.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal))
            {
                problems.Add(
                    "the prefab was built from a different model " +
                    $"({registry.BuildSignature} against " +
                    $"{manifest.build_signature})");
            }

            //  Everything a model out of Blender must not carry. These
            //  are owned by the layout plan, and a duplicate arriving in
            //  an FBX would fight it silently rather than fail.
            AppendForbidden<Collider>(prefab, problems, "collider");
            AppendForbidden<Light>(prefab, problems, "light");
            AppendForbidden<Camera>(prefab, problems, "camera");
            AppendForbidden<Rigidbody>(prefab, problems, "rigidbody");
            AppendForbidden<Animator>(prefab, problems, "animator");

            Renderer[] renderers =
                prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > MaximumRenderers)
            {
                problems.Add(
                    $"{renderers.Length} renderers exceed the " +
                    $"{MaximumRenderers} allowed");
            }

            if (registry.SourceTriangleCount > MaximumTriangles)
            {
                problems.Add(
                    $"{registry.SourceTriangleCount} triangles exceed " +
                    $"the {MaximumTriangles} allowed");
            }

            foreach (Renderer renderer in renderers)
            {
                if (renderer.sharedMaterial == null)
                {
                    problems.Add(
                        $"renderer '{renderer.name}' has no material");
                }
            }

            if (checkRoomDimensions)
            {
                AppendDimensionProblems(registry.Dimensions, problems);
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    $"'{prefabPath}' failed validation:" +
                    Environment.NewLine + "  " +
                    string.Join(Environment.NewLine + "  ", problems));
            }
        }

        private static void AppendDimensionProblems(
            BarRoomDimensions dimensions,
            List<string> problems)
        {
            AppendIfFar(
                dimensions.Width, ExpectedWidth, "room width", problems);
            AppendIfFar(
                dimensions.Depth, ExpectedDepth, "room depth", problems);
            AppendIfFar(
                dimensions.Height, ExpectedHeight, "room height", problems);
            AppendIfFar(
                dimensions.WallThickness,
                ExpectedWallThickness,
                "wall thickness",
                problems);
            AppendIfFar(
                dimensions.DoorWidth,
                ExpectedDoorWidth,
                "door width",
                problems);
        }

        private static void AppendIfFar(
            float actual,
            float expected,
            string label,
            List<string> problems)
        {
            if (Mathf.Abs(actual - expected) > 0.001f)
            {
                problems.Add(
                    $"{label} reads {actual:0.###} m against the " +
                    $"planner's {expected:0.###} m");
            }
        }

        private static void AppendForbidden<TComponent>(
            GameObject prefab,
            List<string> problems,
            string label)
            where TComponent : Component
        {
            TComponent[] found =
                prefab.GetComponentsInChildren<TComponent>(true);
            if (found.Length > 0)
            {
                problems.Add(
                    $"the model carries {found.Length} {label}(s), " +
                    $"first on '{found[0].name}' - collision, light and " +
                    "framing belong to the layout plan");
            }
        }

        private static void BuildPrefab(
            BarManifest manifest,
            string modelPath,
            string prefabPath,
            string rootName)
        {
            GameObject modelAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not import '{modelPath}'.");
            }

            Material sharedLit =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedLitMaterialPath);
            Material sharedEmission =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedEmissionMaterialPath);
            if (sharedLit == null || sharedEmission == null)
            {
                throw new InvalidOperationException(
                    "Bar shared material dependencies failed to load.");
            }

            var root = new GameObject(rootName);
            try
            {
                var model =
                    PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate '{modelPath}'.");
                }

                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(
                    0f,
                    manifest.runtime_wrapper_yaw_degrees,
                    0f);
                model.transform.localScale = Vector3.one;

                Dictionary<string, Renderer> renderers =
                    IndexUniqueRenderers(model);
                Dictionary<string, Transform> transforms =
                    IndexUniqueTransforms(model);

                if (manifest.parts == null ||
                    manifest.parts.Length != renderers.Count)
                {
                    throw new InvalidOperationException(
                        $"The bar model has {renderers.Count} renderers " +
                        $"against {manifest.parts?.Length ?? 0} in the " +
                        "manifest.");
                }

                var bindings = new List<BarPartBinding>();
                foreach (BarManifestPart part in manifest.parts)
                {
                    if (!renderers.TryGetValue(
                            part.name,
                            out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            $"Bar part '{part.name}' is in the manifest " +
                            "but not in the model.");
                    }

                    //  One shared material per family. The sheet and the
                    //  district tint arrive at runtime through a property
                    //  block, exactly as they do for the primitives this
                    //  model replaces - which is why the FBX must not
                    //  bring materials of its own.
                    renderer.sharedMaterial =
                        part.emissive ? sharedEmission : sharedLit;
                    renderer.shadowCastingMode = part.shadows
                        ? ShadowCastingMode.On
                        : ShadowCastingMode.Off;
                    renderer.receiveShadows = part.shadows;

                    var colliders = new List<BarColliderSpec>();
                    foreach (BarManifestCollider spec in
                             part.colliders ??
                             Array.Empty<BarManifestCollider>())
                    {
                        colliders.Add(new BarColliderSpec(
                            ToVector(spec.center),
                            ToVector(spec.size)));
                    }

                    bindings.Add(new BarPartBinding(
                        part.name,
                        part.role,
                        part.group,
                        part.sheet,
                        part.emissive,
                        part.shadows,
                        new BarTintSpec(
                            part.tint.field,
                            ToColor(part.tint.rgb),
                            part.tint.scale,
                            part.tint.lerp_field,
                            ToColor(part.tint.lerp_rgb),
                            part.tint.lerp_t),
                        colliders.ToArray(),
                        renderer));
                }

                var anchors = new List<BarAnchorBinding>();
                foreach (BarManifestAnchor anchor in
                         manifest.anchors ?? Array.Empty<BarManifestAnchor>())
                {
                    string anchorName = $"ANCHOR_{anchor.name}";
                    if (!transforms.TryGetValue(
                            anchorName,
                            out Transform transform))
                    {
                        throw new InvalidOperationException(
                            $"Bar anchor '{anchorName}' is in the " +
                            "manifest but not in the model.");
                    }

                    anchors.Add(new BarAnchorBinding(
                        anchor.name,
                        anchor.role,
                        transform));
                }

                Bounds measured = CalculateLocalBounds(
                    root.transform,
                    renderers.Values);
                AssertMeasuresUpToManifest(measured, manifest, prefabPath);

                BarAssetRegistry registry =
                    root.AddComponent<BarAssetRegistry>();
                registry.Configure(
                    ResolveAuthoringRoot(model.transform),
                    anchors
                        .OrderBy(
                            binding => binding.AnchorName,
                            StringComparer.Ordinal)
                        .ToArray(),
                    bindings
                        .OrderBy(
                            binding => binding.SourceName,
                            StringComparer.Ordinal)
                        .ToArray(),
                    measured,
                    new BarRoomDimensions(
                        manifest.dimensions_m.width,
                        manifest.dimensions_m.depth,
                        manifest.dimensions_m.height,
                        manifest.wall_thickness_m,
                        manifest.door_opening_m.width,
                        manifest.door_opening_m.height),
                    manifest.triangle_count,
                    manifest.generator_version,
                    manifest.design_id,
                    manifest.build_signature);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static BarManifest LoadAndValidateManifest(
            string manifestPath,
            string expectedDesignId)
        {
            string json = File.ReadAllText(manifestPath);
            BarManifest manifest = JsonUtility.FromJson<BarManifest>(json);
            if (manifest == null)
            {
                throw new InvalidOperationException(
                    $"'{manifestPath}' is not a bar manifest.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    expectedDesignId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Bar manifest design id '{manifest.design_id}' is " +
                    $"not '{expectedDesignId}'.");
            }

            if (manifest.colliders || manifest.lights || manifest.cameras)
            {
                throw new InvalidOperationException(
                    "The bar generator declares colliders, lights or " +
                    "cameras; the layout plan owns all three.");
            }

            if (manifest.animation_count != 0)
            {
                throw new InvalidOperationException(
                    "The bar model declares animation; the room is " +
                    "static.");
            }

            if (string.IsNullOrEmpty(manifest.build_signature))
            {
                throw new InvalidOperationException(
                    "The bar manifest carries no build signature.");
            }

            return manifest;
        }

        private static Dictionary<string, Renderer> IndexUniqueRenderers(
            GameObject model)
        {
            var result = new Dictionary<string, Renderer>(
                StringComparer.Ordinal);
            foreach (Renderer renderer in
                     model.GetComponentsInChildren<Renderer>(true))
            {
                if (result.ContainsKey(renderer.name))
                {
                    throw new InvalidOperationException(
                        $"The bar model has two renderers named " +
                        $"'{renderer.name}'; names are the whole bridge " +
                        "to the manifest.");
                }

                result.Add(renderer.name, renderer);
            }

            return result;
        }

        private static Dictionary<string, Transform> IndexUniqueTransforms(
            GameObject model)
        {
            var result = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            foreach (Transform transform in
                     model.GetComponentsInChildren<Transform>(true))
            {
                if (!result.ContainsKey(transform.name))
                {
                    result.Add(transform.name, transform);
                }
            }

            return result;
        }

        /// <summary>
        /// Measures what Unity actually imported against what Blender
        /// says it exported.
        ///
        /// This is not belt-and-braces. An FBX carries its unit
        /// conversion as a scale on the authoring root and the inverse on
        /// every part; anything that separates the two - a reparent, a
        /// prefab extraction - changes the model's size by a factor of a
        /// hundred while leaving anchors, collision and every count in the
        /// manifest exactly right. Nothing short of measuring the meshes
        /// catches it.
        /// </summary>
        private static void AssertMeasuresUpToManifest(
            Bounds measured,
            BarManifest manifest,
            string prefabPath)
        {
            if (manifest.bounds_min == null || manifest.bounds_max == null ||
                manifest.bounds_min.Length < 3 ||
                manifest.bounds_max.Length < 3)
            {
                throw new InvalidOperationException(
                    $"'{prefabPath}': the manifest declares no bounds.");
            }

            //  Blender (x, y, z) reaches Unity as (x, z, y).
            var expected = new Bounds();
            expected.SetMinMax(
                new Vector3(
                    manifest.bounds_min[0],
                    manifest.bounds_min[2],
                    manifest.bounds_min[1]),
                new Vector3(
                    manifest.bounds_max[0],
                    manifest.bounds_max[2],
                    manifest.bounds_max[1]));

            if (Vector3.Distance(measured.size, expected.size) > 0.05f)
            {
                throw new InvalidOperationException(
                    $"'{prefabPath}' imported at the wrong size: " +
                    $"{measured.size} against the manifest's " +
                    $"{expected.size}.");
            }
        }

        private static Bounds CalculateLocalBounds(
            Transform root,
            IEnumerable<Renderer> renderers)
        {
            bool started = false;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (Renderer renderer in renderers)
            {
                Bounds worldBounds = renderer.bounds;
                Vector3 center =
                    root.InverseTransformPoint(worldBounds.center);
                var local = new Bounds(center, worldBounds.size);
                if (!started)
                {
                    bounds = local;
                    started = true;
                }
                else
                {
                    bounds.Encapsulate(local);
                }
            }

            return bounds;
        }

        /// <summary>
        /// The node the generator's own parts actually hang from.
        ///
        /// Importing an FBX gives a wrapper named after the file, and the
        /// generator's `ROOT_` empty sits inside it. `ModelRoot` has to
        /// be the inner one, because the runtime walks its children to
        /// find groups and to flatten parts into the room - and against
        /// the wrapper that walk finds exactly one child and nothing
        /// else, which fails silently: no district dressing, no jukebox,
        /// no fan, and a room that still looks broadly right.
        /// </summary>
        private static Transform ResolveAuthoringRoot(Transform wrapper)
        {
            foreach (Transform child in wrapper)
            {
                if (child.name.StartsWith(
                        "ROOT_",
                        StringComparison.Ordinal))
                {
                    return child;
                }
            }

            throw new InvalidOperationException(
                "The bar model has no ROOT_ node; the generator's export " +
                "selection changed.");
        }

        private static Vector3 ToVector(float[] values)
        {
            return values != null && values.Length >= 3
                ? new Vector3(values[0], values[1], values[2])
                : Vector3.zero;
        }

        private static Color ToColor(float[] values)
        {
            return values != null && values.Length >= 3
                ? new Color(values[0], values[1], values[2], 1f)
                : Color.white;
        }

        private static void RunQueuedBuild()
        {
            buildQueued = false;
            if (!SourcesExist() || EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                return;
            }

            try
            {
                BuildOrThrow();
                AssetDatabase.SaveAssets();
            }
            catch (Exception error)
            {
                Debug.LogError(error);
            }
        }

        private static void EnsureFolderForAsset(string assetPath)
        {
            string folder = Path.GetDirectoryName(assetPath)
                ?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) ||
                AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] segments = folder.Split('/');
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
    }

    [Serializable]
    internal sealed class BarManifest
    {
        public string generator_version;
        public string design_id;
        public float[] bounds_min;
        public float[] bounds_max;
        public BarManifestDimensions dimensions_m;
        public float wall_thickness_m;
        public BarManifestOpening door_opening_m;
        public float runtime_wrapper_yaw_degrees;
        public bool colliders;
        public bool lights;
        public bool cameras;
        public int animation_count;
        public int triangle_count;
        public BarManifestAnchor[] anchors;
        public BarManifestPart[] parts;
        public string build_signature;
    }

    [Serializable]
    internal sealed class BarManifestDimensions
    {
        public float width;
        public float depth;
        public float height;
    }

    [Serializable]
    internal sealed class BarManifestOpening
    {
        public float width;
        public float height;
    }

    [Serializable]
    internal sealed class BarManifestAnchor
    {
        public string name;
        public string role;
        public float[] local_position;
    }

    [Serializable]
    internal sealed class BarManifestPart
    {
        public string name;
        public string role;
        public string group;
        public string sheet;
        public bool emissive;
        public bool shadows;
        public BarManifestTint tint;
        public BarManifestCollider[] colliders;
        public int vertices;
        public int triangles;
    }

    [Serializable]
    internal sealed class BarManifestTint
    {
        public string field;
        public float[] rgb;
        public float scale;
        public string lerp_field;
        public float[] lerp_rgb;
        public float lerp_t;
    }

    [Serializable]
    internal sealed class BarManifestCollider
    {
        public float[] center;
        public float[] size;
    }
}
