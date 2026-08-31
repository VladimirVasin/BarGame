using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Contract of the generated dome, packed density data and passive prefab.
    /// Runtime motion/profile tests live with the cloud field; this fixture
    /// owns only the imported-art boundary.
    /// </summary>
    public sealed class ExteriorCloudAssetTests
    {
        private const string ModelPath =
            "Assets/Environment/Clouds/Models/" +
            "ExteriorCloudDome3D.fbx";
        private const string ManifestPath =
            "Assets/Environment/Clouds/Models/" +
            "ExteriorCloudDome3D.json";
        private const string TexturePath =
            "Assets/Environment/Clouds/Textures/" +
            "ExteriorCloudDensity.png";
        private const string MaterialPath =
            "Assets/Environment/Clouds/Materials/" +
            "ExteriorCloud.mat";
        private const string PrefabPath =
            "Assets/Resources/Environment/ExteriorCloudDome.prefab";
        private const string ShaderPath =
            "Assets/Resources/Shaders/ExteriorCloud.shader";

        [Test]
        public void GeneratorManifest_DefinesOnePassiveUnitDome()
        {
            CloudManifest manifest = LoadManifest();
            Assert.That(
                manifest.design_id,
                Is.EqualTo(ExteriorCloudAssetMetadata.DesignId));
            Assert.That(
                manifest.generator_version,
                Is.EqualTo(ExteriorCloudAssetMetadata.GeneratorVersion));
            Assert.That(
                manifest.mesh_name,
                Is.EqualTo(ExteriorCloudAssetMetadata.MeshName));
            Assert.That(manifest.mesh_count, Is.EqualTo(1));
            Assert.That(manifest.vertex_count, Is.EqualTo(121));
            Assert.That(
                manifest.triangle_count,
                Is.EqualTo(
                    ExteriorCloudAssetMetadata.ExpectedTriangleCount));
            Assert.That(manifest.unit_radius_m, Is.EqualTo(1f));
            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.rigidbodies, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);
            Assert.That(manifest.imported_materials, Is.False);
            Assert.That(manifest.build_signature, Has.Length.EqualTo(64));
            Assert.That(
                manifest.bounds_unity_min,
                Is.EqualTo(new[] { -1f, 0f, -1f }));
            Assert.That(
                manifest.bounds_unity_max,
                Is.EqualTo(new[] { 1f, 1f, 1f }));
        }

        [Test]
        public void DensityTexture_IsTheLinearPackedFileInTheManifest()
        {
            CloudManifest manifest = LoadManifest();
            Assert.That(manifest.texture_size, Is.EqualTo(256));
            Assert.That(manifest.texture_linear_data, Is.True);
            Assert.That(
                manifest.texture_channels.Select(channel => channel.name),
                Is.EqualTo(new[] { "broad", "detail", "erosion" }));
            Assert.That(
                ComputeSha256(TexturePath),
                Is.EqualTo(manifest.texture_sha256)
                    .IgnoreCase);

            Texture2D texture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            var importer =
                AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            Assert.That(texture, Is.Not.Null);
            Assert.That(importer, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(256));
            Assert.That(texture.height, Is.EqualTo(256));
            Assert.That(importer.sRGBTexture, Is.False);
            Assert.That(
                importer.alphaSource,
                Is.EqualTo(TextureImporterAlphaSource.None));
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.streamingMipmaps, Is.False);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(importer.anisoLevel, Is.EqualTo(1));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.maxTextureSize, Is.EqualTo(256));
        }

        [Test]
        public void ImportedModel_IsOneFogShellAtMetreScale()
        {
            CloudManifest manifest = LoadManifest();
            Mesh[] meshes = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<Mesh>()
                .ToArray();
            Assert.That(meshes, Has.Length.EqualTo(1));
            Mesh mesh = meshes[0];
            Assert.That(
                mesh.name,
                Is.EqualTo(ExteriorCloudAssetMetadata.MeshName));
            Assert.That(mesh.isReadable, Is.False);
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(
                mesh.GetIndexCount(0) / 3u,
                Is.EqualTo((uint)manifest.triangle_count));
            Assert.That(
                Vector3.Distance(
                    mesh.bounds.min,
                    new Vector3(-1f, 0f, -1f)),
                Is.LessThanOrEqualTo(0.002f));
            Assert.That(
                Vector3.Distance(mesh.bounds.max, Vector3.one),
                Is.LessThanOrEqualTo(0.002f));

            var importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(importer.bakeAxisConversion, Is.True);
            Assert.That(
                importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.None));
        }

        [Test]
        public void Shader_IsFogExemptAndOwnsTheSharedPropertyContract()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.name, Is.EqualTo("Bar Promenade/Exterior Cloud"));

            string source = File.ReadAllText(ShaderPath);
            Assert.That(source, Does.Contain("Transparent-200"));
            Assert.That(source, Does.Contain("ZWrite Off"));
            Assert.That(source, Does.Contain("ZTest LEqual"));
            Assert.That(source, Does.Contain("Cull Front"));
            Assert.That(source, Does.Not.Contain("multi_compile_fog"));
            Assert.That(source, Does.Not.Contain("MixFog"));
        }

        [Test]
        public void BuiltPrefab_IsOnePassiveSharedRenderer()
        {
            GameObject prefab = LoadBuiltPrefab();
            ExteriorCloudAssetMetadata metadata =
                prefab.GetComponent<ExteriorCloudAssetMetadata>();
            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata.IsComplete, Is.True);
            Assert.That(
                metadata.BuildSignature,
                Is.EqualTo(LoadManifest().build_signature));

            MeshFilter[] filters =
                prefab.GetComponentsInChildren<MeshFilter>(true);
            MeshRenderer[] renderers =
                prefab.GetComponentsInChildren<MeshRenderer>(true);
            Assert.That(filters, Has.Length.EqualTo(1));
            Assert.That(renderers, Has.Length.EqualTo(1));
            Assert.That(metadata.DomeFilter, Is.SameAs(filters[0]));
            Assert.That(metadata.DomeRenderer, Is.SameAs(renderers[0]));
            Assert.That(
                metadata.DomeMesh.name,
                Is.EqualTo(ExteriorCloudAssetMetadata.MeshName));
            Assert.That(
                metadata.SourceTriangleCount,
                Is.EqualTo(
                    ExteriorCloudAssetMetadata.ExpectedTriangleCount));

            MeshRenderer renderer = renderers[0];
            Assert.That(
                renderer.shadowCastingMode,
                Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(renderer.receiveShadows, Is.False);
            Assert.That(
                renderer.lightProbeUsage,
                Is.EqualTo(LightProbeUsage.Off));
            Assert.That(
                renderer.reflectionProbeUsage,
                Is.EqualTo(ReflectionProbeUsage.Off));
            Assert.That(
                renderer.motionVectorGenerationMode,
                Is.EqualTo(MotionVectorGenerationMode.ForceNoMotion));
            Assert.That(renderer.allowOcclusionWhenDynamic, Is.False);

            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Camera>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Animator>(true),
                Is.Empty);
        }

        [Test]
        public void BuiltMaterial_BindsThePackedTextureOnce()
        {
            GameObject prefab = LoadBuiltPrefab();
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Texture2D texture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            Assert.That(material, Is.Not.Null);
            Assert.That(texture, Is.Not.Null);
            Assert.That(
                prefab.GetComponent<MeshRenderer>().sharedMaterial,
                Is.SameAs(material));
            Assert.That(material.GetTexture("_CloudTex"), Is.SameAs(texture));
            Assert.That(material.enableInstancing, Is.True);
            Assert.That(material.renderQueue, Is.EqualTo(2800));

            string[] properties =
            {
                "_HazeColor",
                "_CloudShadowColor",
                "_CloudLightColor",
                "_Coverage",
                "_BroadPhase",
                "_DetailPhase",
                "_HorizonFadeStart",
                "_HorizonFadeEnd",
                "_LightningLift"
            };
            foreach (string property in properties)
            {
                Assert.That(
                    material.HasProperty(property),
                    Is.True,
                    property);
            }

            Assert.That(
                Resources.Load<GameObject>(
                    ExteriorCloudAssetMetadata.ResourcePath),
                Is.SameAs(prefab));
        }

        private static GameObject LoadBuiltPrefab()
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Assert.Ignore(
                    "Exterior cloud prefab has not been built by its " +
                    "editor setup yet.");
            }

            return prefab;
        }

        private static CloudManifest LoadManifest()
        {
            Assert.That(
                File.Exists(ManifestPath),
                Is.True,
                "Exterior cloud generator has not been run.");
            CloudManifest manifest = JsonUtility.FromJson<CloudManifest>(
                File.ReadAllText(ManifestPath));
            Assert.That(manifest, Is.Not.Null);
            return manifest;
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
            public string texture_sha256;
            public int texture_size;
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
