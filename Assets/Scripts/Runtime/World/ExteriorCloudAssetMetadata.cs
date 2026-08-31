using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The immutable imported-art contract carried by the one shared cloud
    /// prefab. Motion, colour and area profiles remain runtime state and are
    /// supplied through a MaterialPropertyBlock by the cloud field.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExteriorCloudAssetMetadata : MonoBehaviour
    {
        public const string ResourcePath =
            "Environment/ExteriorCloudDome";
        public const string DesignId = "exterior_cloud_dome_v1";
        public const string GeneratorVersion = "1.0.0";
        public const string MeshName = "GEO_ExteriorCloudDome";
        public const int ExpectedTriangleCount = 220;
        public const int ExpectedTextureSize = 256;

        [SerializeField] private string designId = DesignId;
        [SerializeField] private string generatorVersion = GeneratorVersion;
        [SerializeField] private string buildSignature = string.Empty;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private MeshFilter domeFilter;
        [SerializeField] private MeshRenderer domeRenderer;
        [SerializeField] private Texture2D densityTexture;

        public string DesignIdentifier => designId;
        public string SourceGeneratorVersion => generatorVersion;
        public string BuildSignature => buildSignature;
        public int SourceTriangleCount => sourceTriangleCount;
        public MeshFilter DomeFilter => domeFilter;
        public MeshRenderer DomeRenderer => domeRenderer;
        public Texture2D DensityTexture => densityTexture;
        public Mesh DomeMesh => domeFilter == null
            ? null
            : domeFilter.sharedMesh;
        public Material SharedMaterial => domeRenderer == null
            ? null
            : domeRenderer.sharedMaterial;

        public bool IsComplete =>
            string.Equals(designId, DesignId, StringComparison.Ordinal) &&
            string.Equals(
                generatorVersion,
                GeneratorVersion,
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(buildSignature) &&
            buildSignature.Length == 64 &&
            sourceTriangleCount == ExpectedTriangleCount &&
            domeFilter != null &&
            domeRenderer != null &&
            domeFilter.sharedMesh != null &&
            string.Equals(
                domeFilter.sharedMesh.name,
                MeshName,
                StringComparison.Ordinal) &&
            domeRenderer.sharedMaterial != null &&
            densityTexture != null;

        public static ExteriorCloudAssetMetadata Load()
        {
            GameObject prefab = Resources.Load<GameObject>(ResourcePath);
            return prefab == null
                ? null
                : prefab.GetComponent<ExteriorCloudAssetMetadata>();
        }

        public static ExteriorCloudAssetMetadata LoadOrThrow()
        {
            ExteriorCloudAssetMetadata metadata = Load();
            if (metadata == null || !metadata.IsComplete)
            {
                throw new InvalidOperationException(
                    "Missing or incomplete exterior cloud prefab at " +
                    $"Resources/{ResourcePath}.");
            }

            return metadata;
        }

        internal void Configure(
            string signature,
            int triangleCount,
            MeshFilter filter,
            MeshRenderer renderer,
            Texture2D texture)
        {
            designId = DesignId;
            generatorVersion = GeneratorVersion;
            buildSignature = signature ?? string.Empty;
            sourceTriangleCount = triangleCount;
            domeFilter = filter;
            domeRenderer = renderer;
            densityTexture = texture;
        }
    }
}
