using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Loads and instantiates the one Blender-authored cloud shell. The
    /// imported material and texture stay shared; every changing area value
    /// is written through a MaterialPropertyBlock by
    /// <see cref="ExteriorCloudField"/>.
    /// </summary>
    public static class ExteriorCloudResources
    {
        public const string PrefabResourcePath =
            ExteriorCloudAssetMetadata.ResourcePath;
        public const string ShaderResourcePath = "Shaders/ExteriorCloud";

        public static ExteriorCloudAssetMetadata LoadMetadata()
        {
            return ExteriorCloudAssetMetadata.Load();
        }

        public static ExteriorCloudAssetMetadata LoadMetadataOrThrow()
        {
            ExteriorCloudAssetMetadata metadata =
                ExteriorCloudAssetMetadata.LoadOrThrow();
            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null || !shader.isSupported)
            {
                throw new InvalidOperationException(
                    "Missing or unsupported exterior cloud shader at " +
                    $"Resources/{ShaderResourcePath}.");
            }

            if (metadata.SharedMaterial == null ||
                metadata.SharedMaterial.shader != shader)
            {
                throw new InvalidOperationException(
                    "The exterior cloud prefab is not bound to its " +
                    "dedicated shader.");
            }

            return metadata;
        }

        public static Material SharedMaterial =>
            LoadMetadataOrThrow().SharedMaterial;

        public static ExteriorCloudAssetMetadata InstantiateDome(
            Transform parent)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            ExteriorCloudAssetMetadata template = LoadMetadataOrThrow();
            GameObject instance = Object.Instantiate(
                template.gameObject,
                parent,
                false);
            instance.name = "Authored Exterior Cloud Dome";
            ExteriorCloudAssetMetadata metadata =
                instance.GetComponent<ExteriorCloudAssetMetadata>();
            if (metadata == null || !metadata.IsComplete)
            {
                DestroyObject(instance);
                throw new InvalidOperationException(
                    "The instantiated exterior cloud prefab lost its " +
                    "asset metadata contract.");
            }

            MeshRenderer renderer = metadata.DomeRenderer;
            renderer.sharedMaterial = template.SharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            return metadata;
        }

        private static void DestroyObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(gameObject);
            }
            else
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
