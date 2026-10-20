using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Offline unique UV0 art and its aligned physical response; no runtime image baking.</summary>
    [Serializable]
    public sealed class MothersHouseInteriorSurfaceContract
    {
        public const string ShaderName = "Bar Promenade/Mothers House Surface";
        public const string AlbedoResourcePath = "MothersHouse/Textures/MothersHouseSurfaceAtlas";
        public const string ResponseResourcePath = "MothersHouse/Textures/MothersHouseMaterialResponse";
        public const int AlbedoSize = 4096;
        public const int ResponseSize = 2048;
        public const long TextureBytesBudget = 30L * 1024 * 1024;
        [SerializeField] private Texture2D albedo;
        [SerializeField] private Texture2D response;
        [SerializeField] private Texture2D auxiliary;
        [SerializeField] private string uvSignature;
        [SerializeField] private string albedoSha256;
        [SerializeField] private string responseSha256;
        [SerializeField] private string modelSha256;
        [SerializeField] private string auxiliarySha256;
        [SerializeField] private int uniquePartCount;
        public Texture2D Albedo => albedo;
        public Texture2D Response => response;
        public Texture2D Auxiliary => auxiliary;
        public string UVSignature => uvSignature;
        public string AlbedoSha256 => albedoSha256;
        public string ResponseSha256 => responseSha256;
        public string ModelSha256 => modelSha256;
        public string AuxiliarySha256 => auxiliarySha256;
        public int UniquePartCount => uniquePartCount;
        public long GpuBytes => Bc7Bytes(albedo) + Bc7Bytes(response) + Bc7Bytes(auxiliary);
        public bool IsConfigured => ValidTexture(albedo, AlbedoSize, FilterMode.Trilinear) &&
            ValidTexture(response, ResponseSize, FilterMode.Trilinear) && ValidTexture(auxiliary, 1024, FilterMode.Bilinear) &&
            IsHash(uvSignature) && IsHash(albedoSha256) && IsHash(responseSha256) && IsHash(modelSha256) &&
            IsHash(auxiliarySha256) &&
            uniquePartCount > 0 && uniquePartCount <= 160 && GpuBytes <= TextureBytesBudget;

        public MothersHouseInteriorSurfaceContract(Texture2D configuredAlbedo, Texture2D configuredResponse, Texture2D configuredAuxiliary,
            string configuredUvSignature, string configuredAlbedoSha256, string configuredResponseSha256,
            string configuredModelSha256, string configuredAuxiliarySha256, int configuredUniquePartCount)
        {
            albedo = configuredAlbedo; response = configuredResponse; uvSignature = configuredUvSignature;
            albedoSha256 = configuredAlbedoSha256; responseSha256 = configuredResponseSha256;
            modelSha256 = configuredModelSha256; uniquePartCount = configuredUniquePartCount;
            auxiliary = configuredAuxiliary; auxiliarySha256 = configuredAuxiliarySha256;
        }

        public static long Bc7Bytes(Texture2D texture)
        {
            if (texture == null) return 0;
            long total = 0;
            int width = texture.width, height = texture.height;
            for (int mip = 0; mip < texture.mipmapCount; mip++)
            {
                total += ((width + 3L) / 4L) * ((height + 3L) / 4L) * 16L;
                width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
            }
            return total;
        }

        private static bool ValidTexture(Texture2D texture, int size, FilterMode filter) => texture != null &&
            texture.width == size && texture.height == size && texture.format == TextureFormat.BC7 &&
            texture.mipmapCount == FullMipCount(size) && !texture.isReadable &&
            texture.wrapMode == TextureWrapMode.Clamp && texture.filterMode == filter;
        private static int FullMipCount(int size)
        {
            int count = 1;
            for (; size > 1; size /= 2) count++;
            return count;
        }
        private static bool IsHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char digit in value)
                if (!(digit >= '0' && digit <= '9') && !(digit >= 'a' && digit <= 'f')) return false;
            return true;
        }
    }
}
