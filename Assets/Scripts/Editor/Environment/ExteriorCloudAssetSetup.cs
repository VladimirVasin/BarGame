using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BarPromenade;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Imports the deterministic unit cloud dome, verifies the packed density
    /// texture against the generator manifest, and builds the one passive
    /// Resources prefab shared by every exterior area.
    /// </summary>
    [InitializeOnLoad]
    public static class ExteriorCloudAssetSetup
    {
        public const string ModelPath =
            "Assets/Environment/Clouds/Models/" +
            "ExteriorCloudDome3D.fbx";
        public const string ManifestPath =
            "Assets/Environment/Clouds/Models/" +
            "ExteriorCloudDome3D.json";
        public const string TexturePath =
            "Assets/Environment/Clouds/Textures/" +
            "ExteriorCloudDensity.png";
        public const string ShaderPath =
            "Assets/Resources/Shaders/ExteriorCloud.shader";
        public const string MaterialPath =
            "Assets/Environment/Clouds/Materials/" +
            "ExteriorCloud.mat";
        public const string PrefabPath =
            "Assets/Resources/Environment/ExteriorCloudDome.prefab";

        public const string ShaderName =
            "Bar Promenade/Exterior Cloud";
        public const int RenderQueue = 2800;

        private const int ExpectedVertexCount = 121;
        private const int MinimumTriangleCount = 180;
        private const int MaximumTriangleCount = 260;
        private const float BoundsTolerance = 0.002f;

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static ExteriorCloudAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem(
            "Bar Promenade/Environment/Exterior Clouds/Build Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log(
                $"Exterior cloud prefab rebuilt at '{PrefabPath}'.");
        }

        public static void RunBatch()
        {
            BuildOrThrow();
            Debug.Log("EXTERIOR CLOUD UNITY ASSET BUILD OK");
        }

        [MenuItem(
            "Bar Promenade/Environment/Exterior Clouds/" +
            "Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("Exterior cloud imported asset contract is valid.");
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) &&
                File.Exists(ManifestPath) &&
                File.Exists(TexturePath) &&
                File.Exists(ShaderPath);
        }

        public static bool IsSourcePath(string path)
        {
            return string.Equals(
                    path,
                    ModelPath,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    path,
                    ManifestPath,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    path,
                    TexturePath,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    path,
                    ShaderPath,
                    StringComparison.OrdinalIgnoreCase);
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
                    "Exterior cloud setup requires its generated FBX, " +
                    "manifest, packed density texture and shader. Run " +
                    "tools/build-exterior-cloud-3d-model.py first.");
            }

            isBuilding = true;
            try
            {
                EnsureFolderForAsset(MaterialPath);
                EnsureFolderForAsset(PrefabPath);
                ImportSources();
                CloudManifest manifest = LoadAndValidateManifest();
                Mesh mesh = LoadAndValidateMesh(manifest);
                Texture2D texture = LoadAndValidateTexture(manifest);
                Material material = BuildMaterial(texture);
                BuildPrefab(mesh, material, texture, manifest);
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
            CloudManifest manifest = LoadAndValidateManifest();
            Mesh mesh = LoadAndValidateMesh(manifest);
            Texture2D texture = LoadAndValidateTexture(manifest);
            Material material = LoadAndValidateMaterial(texture);
            ValidatePrefab(manifest, mesh, texture, material);
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
                    "Could not build the exterior cloud prefab: " +
                    exception);
            }
        }

        private static void ValidateDependencyStamp()
        {
            if (isBuilding ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                !SourcesExist())
            {
                return;
            }

            CloudManifest manifest;
            try
            {
                manifest = LoadAndValidateManifest();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Exterior cloud generated sources are invalid: " +
                    exception);
                return;
            }

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            ExteriorCloudAssetMetadata metadata = prefab == null
                ? null
                : prefab.GetComponent<ExteriorCloudAssetMetadata>();
            if (metadata == null ||
                !metadata.IsComplete ||
                !string.Equals(
                    metadata.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal))
            {
                QueueBuildWhenSourcesExist();
            }
        }

        private static void ImportSources()
        {
            foreach (string path in new[]
                     {
                         ShaderPath,
                         TexturePath,
                         ModelPath,
                         ManifestPath
                     })
            {
                AssetDatabase.ImportAsset(
                    path,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static CloudManifest LoadAndValidateManifest()
        {
            CloudManifest manifest = JsonUtility.FromJson<CloudManifest>(
                File.ReadAllText(ManifestPath));
            if (manifest == null ||
                manifest.bounds_unity_min == null ||
                manifest.bounds_unity_max == null ||
                manifest.texture_channels == null)
            {
                throw new InvalidOperationException(
                    "Exterior cloud manifest is missing or malformed.");
            }

            var problems = new List<string>();
            if (!string.Equals(
                    manifest.design_id,
                    ExteriorCloudAssetMetadata.DesignId,
                    StringComparison.Ordinal))
            {
                problems.Add(
                    $"design '{manifest.design_id}', expected " +
                    $"'{ExteriorCloudAssetMetadata.DesignId}'");
            }
            if (!string.Equals(
                    manifest.generator_version,
                    ExteriorCloudAssetMetadata.GeneratorVersion,
                    StringComparison.Ordinal))
            {
                problems.Add(
                    $"generator '{manifest.generator_version}', expected " +
                    $"'{ExteriorCloudAssetMetadata.GeneratorVersion}'");
            }
            if (!string.Equals(
                    manifest.mesh_name,
                    ExteriorCloudAssetMetadata.MeshName,
                    StringComparison.Ordinal) ||
                manifest.mesh_count != 1)
            {
                problems.Add("manifest must describe the one dome mesh");
            }
            if (manifest.vertex_count != ExpectedVertexCount)
            {
                problems.Add(
                    $"authored vertex count {manifest.vertex_count}, " +
                    $"expected {ExpectedVertexCount}");
            }
            if (manifest.triangle_count !=
                    ExteriorCloudAssetMetadata.ExpectedTriangleCount ||
                manifest.triangle_count < MinimumTriangleCount ||
                manifest.triangle_count > MaximumTriangleCount)
            {
                problems.Add(
                    $"triangle count {manifest.triangle_count} is invalid");
            }
            if (Mathf.Abs(manifest.unit_radius_m - 1f) > 0.0001f)
            {
                problems.Add(
                    $"unit radius is {manifest.unit_radius_m:0.####} m");
            }
            if (manifest.texture_size !=
                    ExteriorCloudAssetMetadata.ExpectedTextureSize ||
                !string.Equals(
                    manifest.texture_file,
                    Path.GetFileName(TexturePath),
                    StringComparison.Ordinal) ||
                !manifest.texture_linear_data ||
                manifest.texture_channels.Length != 3)
            {
                problems.Add("packed density texture contract drifted");
            }
            string[] channelNames = { "broad", "detail", "erosion" };
            for (int index = 0;
                 index < Math.Min(
                     channelNames.Length,
                     manifest.texture_channels.Length);
                 index++)
            {
                if (!string.Equals(
                        manifest.texture_channels[index].name,
                        channelNames[index],
                        StringComparison.Ordinal))
                {
                    problems.Add(
                        $"texture channel {index} is not " +
                        $"'{channelNames[index]}'");
                }
            }
            if (!IsSha256(manifest.texture_sha256) ||
                !IsSha256(manifest.build_signature))
            {
                problems.Add("manifest signatures are not SHA-256 values");
            }
            if (manifest.colliders ||
                manifest.rigidbodies ||
                manifest.lights ||
                manifest.cameras ||
                manifest.animation_count != 0 ||
                manifest.imported_materials)
            {
                problems.Add(
                    "generated dome must remain passive presentation data");
            }
            if (manifest.bounds_unity_min.Length != 3 ||
                manifest.bounds_unity_max.Length != 3)
            {
                problems.Add("Unity bounds must contain three components");
            }

            string measuredTextureHash = ComputeSha256(TexturePath);
            if (!string.Equals(
                    measuredTextureHash,
                    manifest.texture_sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(
                    "packed density texture bytes do not match manifest");
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Exterior cloud manifest contract violated:\n  - " +
                    string.Join("\n  - ", problems));
            }

            return manifest;
        }

        private static Mesh LoadAndValidateMesh(CloudManifest manifest)
        {
            Mesh[] meshes = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<Mesh>()
                .ToArray();
            if (meshes.Length != 1 ||
                !string.Equals(
                    meshes[0].name,
                    ExteriorCloudAssetMetadata.MeshName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Exterior cloud FBX must import exactly one mesh named " +
                    $"'{ExteriorCloudAssetMetadata.MeshName}'.");
            }

            Mesh mesh = meshes[0];
            var problems = new List<string>();
            if (mesh.isReadable)
            {
                problems.Add("dome mesh must import non-readable");
            }
            if (mesh.subMeshCount != 1 ||
                mesh.GetIndexCount(0) / 3u !=
                    (uint)manifest.triangle_count)
            {
                problems.Add("imported triangle count differs from manifest");
            }
            Vector3 expectedMin = ToVector3(manifest.bounds_unity_min);
            Vector3 expectedMax = ToVector3(manifest.bounds_unity_max);
            if (Vector3.Distance(mesh.bounds.min, expectedMin) >
                    BoundsTolerance ||
                Vector3.Distance(mesh.bounds.max, expectedMax) >
                    BoundsTolerance)
            {
                problems.Add(
                    $"imported bounds {mesh.bounds.min}..{mesh.bounds.max} " +
                    $"differ from {expectedMin}..{expectedMax}");
            }

            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null ||
                importer.animationType != ModelImporterAnimationType.None ||
                importer.importAnimation ||
                importer.importCameras ||
                importer.importLights ||
                importer.addCollider ||
                importer.importBlendShapes ||
                importer.importNormals != ModelImporterNormals.Import ||
                importer.importTangents != ModelImporterTangents.None ||
                importer.materialImportMode !=
                    ModelImporterMaterialImportMode.None ||
                !importer.bakeAxisConversion)
            {
                problems.Add("model importer contract drifted");
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Exterior cloud model contract violated:\n  - " +
                    string.Join("\n  - ", problems));
            }

            return mesh;
        }

        private static Texture2D LoadAndValidateTexture(
            CloudManifest manifest)
        {
            Texture2D texture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            TextureImporter importer =
                AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (texture == null || importer == null)
            {
                throw new InvalidOperationException(
                    "Exterior cloud density texture failed to import.");
            }

            var problems = new List<string>();
            if (texture.width != manifest.texture_size ||
                texture.height != manifest.texture_size)
            {
                problems.Add(
                    $"texture imported {texture.width}x{texture.height}");
            }
            if (importer.textureType != TextureImporterType.Default ||
                importer.textureShape != TextureImporterShape.Texture2D ||
                importer.sRGBTexture ||
                importer.alphaSource != TextureImporterAlphaSource.None ||
                importer.alphaIsTransparency ||
                !importer.mipmapEnabled ||
                importer.streamingMipmaps ||
                importer.isReadable ||
                importer.npotScale != TextureImporterNPOTScale.None ||
                importer.wrapMode != TextureWrapMode.Repeat ||
                importer.filterMode != FilterMode.Bilinear ||
                importer.anisoLevel != 1 ||
                importer.textureCompression !=
                    TextureImporterCompression.Uncompressed ||
                importer.maxTextureSize !=
                    ExteriorCloudAssetMetadata.ExpectedTextureSize)
            {
                problems.Add("texture importer contract drifted");
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Exterior cloud texture contract violated:\n  - " +
                    string.Join("\n  - ", problems));
            }

            return texture;
        }

        private static Material BuildMaterial(Texture2D texture)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null ||
                !string.Equals(
                    shader.name,
                    ShaderName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Exterior cloud shader '{ShaderName}' failed to load.");
            }

            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "Exterior Cloud"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
                material.name = "Exterior Cloud";
            }

            material.SetTexture("_CloudTex", texture);
            material.enableInstancing = true;
            material.renderQueue = RenderQueue;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadAndValidateMaterial(Texture2D texture)
        {
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null ||
                material.shader == null ||
                !string.Equals(
                    material.shader.name,
                    ShaderName,
                    StringComparison.Ordinal) ||
                material.GetTexture("_CloudTex") != texture ||
                !material.enableInstancing ||
                material.renderQueue != RenderQueue)
            {
                throw new InvalidOperationException(
                    "Exterior cloud shared material is missing or stale.");
            }

            string[] requiredProperties =
            {
                "_CloudTex",
                "_HazeColor",
                "_CloudShadowColor",
                "_CloudLightColor",
                "_Coverage",
                "_EdgeSoftness",
                "_Opacity",
                "_BroadScale",
                "_DetailScale",
                "_DetailStrength",
                "_ErosionStrength",
                "_BroadPhase",
                "_DetailPhase",
                "_HorizonFadeStart",
                "_HorizonFadeEnd",
                "_LightningLift"
            };
            if (requiredProperties.Any(property =>
                    !material.HasProperty(property)))
            {
                throw new InvalidOperationException(
                    "Exterior cloud shader property contract drifted.");
            }

            return material;
        }

        private static void BuildPrefab(
            Mesh mesh,
            Material material,
            Texture2D texture,
            CloudManifest manifest)
        {
            var root = new GameObject("Exterior Cloud Dome");
            try
            {
                MeshFilter filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode =
                    MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = false;

                ExteriorCloudAssetMetadata metadata =
                    root.AddComponent<ExteriorCloudAssetMetadata>();
                metadata.Configure(
                    manifest.build_signature,
                    manifest.triangle_count,
                    filter,
                    renderer,
                    texture);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    PrefabPath,
                    out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save cloud prefab at '{PrefabPath}'.");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void ValidatePrefab(
            CloudManifest manifest,
            Mesh mesh,
            Texture2D texture,
            Material material)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Exterior cloud prefab is missing at '{PrefabPath}'.");
            }

            var problems = new List<string>();
            ExteriorCloudAssetMetadata metadata =
                prefab.GetComponent<ExteriorCloudAssetMetadata>();
            MeshFilter[] filters =
                prefab.GetComponentsInChildren<MeshFilter>(true);
            MeshRenderer[] renderers =
                prefab.GetComponentsInChildren<MeshRenderer>(true);
            if (metadata == null ||
                !metadata.IsComplete ||
                !string.Equals(
                    metadata.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal) ||
                metadata.SourceTriangleCount != manifest.triangle_count ||
                metadata.DensityTexture != texture)
            {
                problems.Add("prefab metadata is incomplete or stale");
            }
            if (filters.Length != 1 ||
                renderers.Length != 1 ||
                filters[0].sharedMesh != mesh ||
                renderers[0].sharedMaterial != material)
            {
                problems.Add("prefab must own one bound mesh renderer");
            }
            if (renderers.Length == 1 &&
                (renderers[0].shadowCastingMode != ShadowCastingMode.Off ||
                 renderers[0].receiveShadows ||
                 renderers[0].lightProbeUsage != LightProbeUsage.Off ||
                 renderers[0].reflectionProbeUsage !=
                     ReflectionProbeUsage.Off ||
                 renderers[0].motionVectorGenerationMode !=
                     MotionVectorGenerationMode.ForceNoMotion ||
                 renderers[0].allowOcclusionWhenDynamic))
            {
                problems.Add("prefab renderer presentation flags drifted");
            }
            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Camera>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AudioSource>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Animator>(true).Length != 0)
            {
                problems.Add("prefab contains a forbidden active component");
            }
            Transform transform = prefab.transform;
            if (transform.localPosition != Vector3.zero ||
                transform.localRotation != Quaternion.identity ||
                transform.localScale != Vector3.one)
            {
                problems.Add("prefab root transform must remain identity");
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Exterior cloud prefab contract violated:\n  - " +
                    string.Join("\n  - ", problems));
            }
        }

        private static Vector3 ToVector3(float[] values)
        {
            return values == null || values.Length != 3
                ? new Vector3(float.NaN, float.NaN, float.NaN)
                : new Vector3(values[0], values[1], values[2]);
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool hexadecimal =
                    character >= '0' && character <= '9' ||
                    character >= 'a' && character <= 'f' ||
                    character >= 'A' && character <= 'F';
                if (!hexadecimal)
                {
                    return false;
                }
            }

            return true;
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return string.Concat(
                    algorithm.ComputeHash(stream)
                        .Select(value => value.ToString("x2")));
            }
        }

        private static void EnsureFolderForAsset(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrWhiteSpace(directory) ||
                Directory.Exists(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
        }

        [Serializable]
        private sealed class CloudManifest
        {
            public string generator_version;
            public string design_id;
            public string mesh_name;
            public int mesh_count;
            public int vertex_count;
            public int triangle_count;
            public float unit_radius_m;
            public float[] bounds_unity_min;
            public float[] bounds_unity_max;
            public string texture_file;
            public int texture_size;
            public string texture_sha256;
            public bool texture_linear_data;
            public CloudTextureChannel[] texture_channels;
            public bool colliders;
            public bool rigidbodies;
            public bool lights;
            public bool cameras;
            public int animation_count;
            public bool imported_materials;
            public string build_signature;
        }

        [Serializable]
        private sealed class CloudTextureChannel
        {
            public string name;
        }
    }
}
