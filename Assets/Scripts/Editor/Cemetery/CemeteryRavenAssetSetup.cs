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
    /// Imports the cemetery raven FBX and builds the passive runtime
    /// prefab outside Resources, binding it to the one addressable
    /// provider asset. The stairwell cat setup's shape, plus the one
    /// thing the cat never had: a detail atlas, whose bytes are
    /// re-hashed against the manifest so a stale or hand-touched PNG
    /// can never ride under a green signature.
    /// </summary>
    [InitializeOnLoad]
    public static class CemeteryRavenAssetSetup
    {
        public const string ModelPath =
            "Assets/Cemetery/Raven/Models/CemeteryRaven3D.fbx";
        public const string ManifestPath =
            "Assets/Cemetery/Raven/Models/CemeteryRaven3D.json";
        public const string AtlasPath =
            "Assets/Cemetery/Raven/Textures/" +
            "CemeteryRavenDetailAtlas.png";
        public const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        public const string PrefabPath =
            "Assets/Cemetery/Raven/Prefabs/CemeteryRaven.prefab";
        public const string ProviderPath =
            "Assets/Resources/Cemetery/CemeteryRavenProvider.asset";

        private const string ExpectedDesignId = "cemetery_raven_v1";
        private const int MinimumTriangleCount = 350;
        private const int MaximumTriangleCount = 700;
        private const int ExpectedAtlasSize = 256;

        /// <summary>
        /// The measured-bounds gate against the manifest's standing
        /// height: the one check that catches a hundredth-scale
        /// import, an axis mishap or a re-authored bird whose prefab
        /// was never rebuilt.
        /// </summary>
        private const float StandingHeightToleranceMeters = 0.035f;

        private static readonly string[] ExpectedPivotNames =
        {
            CemeteryRavenRigAnchors.BodyRootPivotName,
            CemeteryRavenRigAnchors.HeadPivotName,
            CemeteryRavenRigAnchors.WingLeftPivotName,
            CemeteryRavenRigAnchors.WingRightPivotName,
            CemeteryRavenRigAnchors.TailPivotName
        };

        private static readonly string[] ExpectedAnchorNames =
        {
            CemeteryRavenRigAnchors.FeetContactAnchorName
        };

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static CemeteryRavenAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/Cemetery Raven 3D/Build Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log(
                $"Cemetery raven prefab rebuilt at '{PrefabPath}'.");
        }

        [MenuItem("Bar Promenade/Cemetery Raven 3D/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log(
                "Cemetery raven passive prefab contract is valid.");
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) &&
                File.Exists(ManifestPath) &&
                File.Exists(AtlasPath) &&
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
                    "Cemetery raven build requires its FBX, " +
                    "manifest, detail atlas and the shared " +
                    "Player3DLit material.");
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
                AssetDatabase.ImportAsset(
                    AtlasPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                CemeteryRavenManifest manifest =
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
                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        "The shared Player3DLit material failed to " +
                        "load.");
                }

                Texture2D detailAtlas =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(
                        AtlasPath);
                if (detailAtlas == null)
                {
                    throw new InvalidOperationException(
                        $"The raven detail atlas failed to import " +
                        $"from '{AtlasPath}'.");
                }

                BuildPrefab(
                    modelAsset,
                    sharedMaterial,
                    detailAtlas,
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
            CemeteryRavenManifest manifest = LoadAndValidateManifest();

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Cemetery raven prefab is missing at " +
                    $"'{PrefabPath}'.");
            }

            CemeteryRavenRigAnchors anchors =
                prefab.GetComponent<CemeteryRavenRigAnchors>();
            if (anchors == null || !anchors.IsBound)
            {
                throw new InvalidOperationException(
                    "Cemetery raven prefab has no fully bound rig " +
                    "anchors.");
            }

            if (anchors.Renderers.Count != manifest.mesh_count ||
                anchors.RendererBindings.Count != manifest.mesh_count)
            {
                throw new InvalidOperationException(
                    "Cemetery raven renderer counts differ from the " +
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
                    "Cemetery raven rig anchor source metadata is " +
                    "stale.");
            }

            // MEASURE the imported renderers against the manifest:
            // Blender's Z-up standing height becomes prefab Y through
            // the axis bake, so a bird that arrived at a hundredth of
            // its size — or a hundred times it — fails here and
            // nowhere quieter.
            if (Mathf.Abs(
                    anchors.LocalBounds.max.y -
                    manifest.standing_height_m) >
                StandingHeightToleranceMeters ||
                anchors.LocalBounds.min.y < -0.05f)
            {
                throw new InvalidOperationException(
                    "Cemetery raven prefab bounds lost the authored " +
                    "standing height or sole plane.");
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
                    "The passive raven prefab must contain no " +
                    "Collider, Light, Rigidbody, AudioSource or " +
                    "Camera component.");
            }

            Material sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedMaterialPath);
            for (int index = 0;
                 index < anchors.Renderers.Count;
                 index++)
            {
                Renderer renderer = anchors.Renderers[index];
                if (renderer == null ||
                    renderer.sharedMaterials.Length != 1 ||
                    renderer.sharedMaterial != sharedMaterial)
                {
                    throw new InvalidOperationException(
                        "Every raven renderer must carry exactly " +
                        "the one shared Player3DLit material.");
                }

                if (renderer.shadowCastingMode !=
                    ShadowCastingMode.Off)
                {
                    throw new InvalidOperationException(
                        "Raven renderers must not cast shadows, " +
                        "matching every staged character.");
                }
            }

            Texture2D detailAtlas =
                AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            if (detailAtlas == null ||
                anchors.DetailAtlas != detailAtlas)
            {
                throw new InvalidOperationException(
                    "The raven prefab must bind the imported detail " +
                    "atlas on its rig anchors.");
            }

            bool anyAtlasBinding = false;
            for (int index = 0;
                 index < anchors.RendererBindings.Count;
                 index++)
            {
                if (anchors.RendererBindings[index].UsesDetailAtlas)
                {
                    anyAtlasBinding = true;
                    break;
                }
            }

            if (!anyAtlasBinding)
            {
                throw new InvalidOperationException(
                    "No raven renderer samples the detail atlas; " +
                    "the texture would be dead weight.");
            }

            CemeteryRavenProvider provider =
                AssetDatabase.LoadAssetAtPath<CemeteryRavenProvider>(
                    ProviderPath);
            if (provider == null || provider.RavenPrefab != prefab)
            {
                throw new InvalidOperationException(
                    "The raven provider asset must reference the " +
                    "built prefab.");
            }
        }

        private static void ValidateDependencyStamp()
        {
            if (!SourcesExist())
            {
                return;
            }

            CemeteryRavenManifest manifest;
            try
            {
                manifest = LoadAndValidateManifest();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not validate cemetery raven source " +
                    $"manifest: {exception}");
                return;
            }

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            CemeteryRavenRigAnchors anchors = prefab != null
                ? prefab.GetComponent<CemeteryRavenRigAnchors>()
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
                    $"Could not build cemetery raven prefab: " +
                    $"{exception}");
            }
        }

        private static CemeteryRavenManifest LoadAndValidateManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import manifest '{ManifestPath}'.");
            }

            CemeteryRavenManifest manifest =
                JsonUtility.FromJson<CemeteryRavenManifest>(
                    source.text);
            if (manifest == null ||
                manifest.parts == null ||
                manifest.pivot_names == null ||
                manifest.anchor_names == null ||
                manifest.atlas_regions == null)
            {
                throw new InvalidOperationException(
                    "Cemetery raven manifest is malformed.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    ExpectedDesignId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Cemetery raven design differs from the " +
                    "approved source.");
            }

            if (manifest.mesh_count != manifest.parts.Length ||
                manifest.mesh_count < 8 ||
                manifest.mesh_count > 12 ||
                manifest.triangle_count < MinimumTriangleCount ||
                manifest.triangle_count > MaximumTriangleCount ||
                manifest.triangle_budget == null ||
                manifest.triangle_budget.Length != 2 ||
                manifest.triangle_budget[0] != MinimumTriangleCount ||
                manifest.triangle_budget[1] != MaximumTriangleCount)
            {
                throw new InvalidOperationException(
                    "Cemetery raven manifest mesh or triangle " +
                    "budget is invalid.");
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
                    "Cemetery raven must be collider/light/" +
                    "Rigidbody-free, animation-free and reuse the " +
                    "designated shared material.");
            }

            if (!manifest.pivot_names.SequenceEqual(
                    ExpectedPivotNames,
                    StringComparer.Ordinal) ||
                !manifest.anchor_names.SequenceEqual(
                    ExpectedAnchorNames,
                    StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Cemetery raven pivots or anchors diverge from " +
                    "the authored articulation set.");
            }

            // The wing deploy limit is the one contract the generator
            // and the runtime pose rules SHARE; a drift here would be
            // a runtime swinging a wing past the arc the geometry was
            // modelled to sweep, so the build refuses it outright.
            if (Mathf.Abs(
                    manifest.wing_fold_max_degrees -
                    CemeteryRavenPoseRules.WingFoldMaximumDegrees) >
                0.001f)
            {
                throw new InvalidOperationException(
                    "Cemetery raven wing_fold_max_degrees disagrees " +
                    "with CemeteryRavenPoseRules.");
            }

            if (string.IsNullOrWhiteSpace(manifest.build_signature) ||
                manifest.build_signature.Length != 64)
            {
                throw new InvalidOperationException(
                    "Cemetery raven manifest lacks deterministic " +
                    "source metadata.");
            }

            ValidateManifestAtlas(manifest);
            ValidateManifestDesignParts(manifest.parts);
            return manifest;
        }

        /// <summary>
        /// The atlas half of the manifest contract: geometry of the
        /// declared regions, and — the part no other setup needed —
        /// the PNG bytes on disk re-hashed against the recorded
        /// SHA-256, so the imported texture is provably the very file
        /// the generator painted and validated.
        /// </summary>
        private static void ValidateManifestAtlas(
            CemeteryRavenManifest manifest)
        {
            if (!string.Equals(
                    manifest.detail_atlas_file,
                    Path.GetFileName(AtlasPath),
                    StringComparison.Ordinal) ||
                manifest.detail_atlas_size != ExpectedAtlasSize)
            {
                throw new InvalidOperationException(
                    "Cemetery raven manifest names a different " +
                    "detail atlas than the imported one.");
            }

            if (string.IsNullOrWhiteSpace(
                    manifest.detail_atlas_sha256) ||
                manifest.detail_atlas_sha256.Length != 64)
            {
                throw new InvalidOperationException(
                    "Cemetery raven manifest lacks the detail " +
                    "atlas hash.");
            }

            string measured = ComputeAtlasSha256();
            if (!string.Equals(
                    measured,
                    manifest.detail_atlas_sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Detail atlas '{AtlasPath}' hashes {measured} " +
                    $"but the manifest recorded " +
                    $"{manifest.detail_atlas_sha256}.");
            }

            if (manifest.atlas_regions.Length == 0)
            {
                throw new InvalidOperationException(
                    "Cemetery raven manifest declares no atlas " +
                    "regions.");
            }

            var partNames = new HashSet<string>(
                manifest.parts.Select(part => part.name),
                StringComparer.Ordinal);
            for (int index = 0;
                 index < manifest.atlas_regions.Length;
                 index++)
            {
                CemeteryRavenManifestAtlasRegion region =
                    manifest.atlas_regions[index];
                if (region == null ||
                    string.IsNullOrEmpty(region.name) ||
                    string.IsNullOrEmpty(region.layout) ||
                    region.cell == null ||
                    region.cell.Length != 4 ||
                    !partNames.Contains(region.renderer))
                {
                    throw new InvalidOperationException(
                        "Cemetery raven manifest carries a " +
                        "malformed atlas region.");
                }
            }
        }

        private static void ValidateManifestDesignParts(
            IReadOnlyList<CemeteryRavenManifestPart> parts)
        {
            Dictionary<string, CemeteryRavenManifestPart> byName =
                parts.ToDictionary(
                    part => part.name,
                    StringComparer.Ordinal);
            RequirePart(
                byName,
                "GEO_Body",
                "raven_body",
                CemeteryRavenRigAnchors.BodyRootPivotName);
            RequirePart(
                byName,
                "GEO_Head",
                "raven_head",
                CemeteryRavenRigAnchors.HeadPivotName);
            RequirePart(
                byName,
                "GEO_Beak",
                "raven_beak",
                CemeteryRavenRigAnchors.HeadPivotName);
            RequirePart(
                byName,
                "GEO_Eye.L",
                "raven_eye",
                CemeteryRavenRigAnchors.HeadPivotName);
            RequirePart(
                byName,
                "GEO_Eye.R",
                "raven_eye",
                CemeteryRavenRigAnchors.HeadPivotName);
            RequirePart(
                byName,
                "GEO_Wing.L",
                "raven_wing",
                CemeteryRavenRigAnchors.WingLeftPivotName);
            RequirePart(
                byName,
                "GEO_Wing.R",
                "raven_wing",
                CemeteryRavenRigAnchors.WingRightPivotName);
            RequirePart(
                byName,
                "GEO_Tail",
                "raven_tail",
                CemeteryRavenRigAnchors.TailPivotName);
            RequirePart(
                byName,
                "GEO_Leg.L",
                "raven_leg",
                CemeteryRavenRigAnchors.BodyRootPivotName);
            RequirePart(
                byName,
                "GEO_Leg.R",
                "raven_leg",
                CemeteryRavenRigAnchors.BodyRootPivotName);
        }

        private static void RequirePart(
            IReadOnlyDictionary<string, CemeteryRavenManifestPart>
                parts,
            string name,
            string role,
            string pivot)
        {
            if (!parts.TryGetValue(
                    name,
                    out CemeteryRavenManifestPart part) ||
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
                    $"Cemetery raven manifest lost required part " +
                    $"'{name}' with role '{role}' on pivot " +
                    $"'{pivot}'.");
            }
        }

        private static void BuildPrefab(
            GameObject modelAsset,
            Material sharedMaterial,
            Texture2D detailAtlas,
            CemeteryRavenManifest manifest)
        {
            GameObject prefabRoot = new GameObject("CemeteryRaven");
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(modelAsset)
                        as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate the imported raven " +
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
                        "Imported raven renderer count differs from " +
                        "the manifest.");
                }

                var atlasRenderers = new HashSet<string>(
                    manifest.atlas_regions.Select(
                        region => region.renderer),
                    StringComparer.Ordinal);

                List<Renderer> rendererList =
                    new List<Renderer>(manifest.parts.Length);
                List<CemeteryRavenRendererBinding> bindings =
                    new List<CemeteryRavenRendererBinding>(
                        manifest.parts.Length);
                for (int index = 0;
                     index < manifest.parts.Length;
                     index++)
                {
                    CemeteryRavenManifestPart source =
                        manifest.parts[index];
                    if (!renderersByName.TryGetValue(
                            source.name,
                            out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            "Imported raven is missing renderer " +
                            $"'{source.name}'.");
                    }

                    renderer.sharedMaterials = new[]
                    {
                        sharedMaterial
                    };
                    renderer.shadowCastingMode =
                        ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage =
                        ReflectionProbeUsage.Off;
                    renderer.motionVectorGenerationMode =
                        MotionVectorGenerationMode.ForceNoMotion;

                    bindings.Add(
                        new CemeteryRavenRendererBinding(
                            source.name,
                            source.pivot,
                            source.role,
                            source.palette_name,
                            renderer,
                            ParseColor(source.base_color),
                            atlasRenderers.Contains(source.name)));
                    rendererList.Add(renderer);
                }

                Renderer[] renderers = rendererList.ToArray();
                CemeteryRavenRigAnchors anchors = prefabRoot
                    .AddComponent<CemeteryRavenRigAnchors>();
                anchors.Configure(
                    model.transform,
                    renderers,
                    bindings.ToArray(),
                    RequireTransform(
                        transformsByName,
                        CemeteryRavenRigAnchors.BodyRootPivotName),
                    RequireTransform(
                        transformsByName,
                        CemeteryRavenRigAnchors.HeadPivotName),
                    RequireTransform(
                        transformsByName,
                        CemeteryRavenRigAnchors.WingLeftPivotName),
                    RequireTransform(
                        transformsByName,
                        CemeteryRavenRigAnchors.WingRightPivotName),
                    RequireTransform(
                        transformsByName,
                        CemeteryRavenRigAnchors.TailPivotName),
                    RequireTransform(
                        transformsByName,
                        CemeteryRavenRigAnchors.FeetContactAnchorName),
                    detailAtlas,
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
                        "Could not save cemetery raven prefab at " +
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
            CemeteryRavenProvider provider =
                AssetDatabase.LoadAssetAtPath<CemeteryRavenProvider>(
                    ProviderPath);
            if (provider == null)
            {
                provider = ScriptableObject
                    .CreateInstance<CemeteryRavenProvider>();
                AssetDatabase.CreateAsset(provider, ProviderPath);
            }

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            SerializedObject serialized =
                new SerializedObject(provider);
            serialized.FindProperty("ravenPrefab")
                .objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(provider);
        }

        private static string ComputeAtlasSha256()
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(
                    File.ReadAllBytes(AtlasPath));
                var builder =
                    new System.Text.StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    builder.Append(hash[index].ToString("x2"));
                }

                return builder.ToString();
            }
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
                        "Imported raven hierarchy contains " +
                        "duplicate transform name " +
                        $"'{transform.name}'.");
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
                        "Imported raven hierarchy contains " +
                        "duplicate renderer name " +
                        $"'{renderer.name}'.");
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
                "Imported raven hierarchy is missing transform " +
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
                    "Cemetery raven model contains no renderers.");
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
        private sealed class CemeteryRavenManifest
        {
            public string generator;
            public string generator_version;
            public string blender_version;
            public string design_id;
            public string display_name;
            public int seed;
            public float standing_height_m;
            public string forward_axis;
            public string anatomical_left_axis;
            public int mesh_count;
            public int triangle_count;
            public int[] triangle_budget;
            public string[] pivot_names;
            public string[] anchor_names;
            public float[] bounds_min;
            public float[] bounds_max;
            public string material_asset;
            public string detail_atlas_file;
            public int detail_atlas_size;
            public string detail_atlas_sha256;
            public CemeteryRavenManifestAtlasRegion[] atlas_regions;
            public float wing_fold_max_degrees;
            public bool emissive;
            public bool colliders;
            public bool lights;
            public bool rigidbodies;
            public int animation_count;
            public string[] animations;
            public string build_signature;
            public CemeteryRavenManifestPart[] parts;
        }

        [Serializable]
        private sealed class CemeteryRavenManifestAtlasRegion
        {
            public string name;
            public string renderer;
            public int[] cell;
            public string layout;
        }

        [Serializable]
        private sealed class CemeteryRavenManifestPart
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
