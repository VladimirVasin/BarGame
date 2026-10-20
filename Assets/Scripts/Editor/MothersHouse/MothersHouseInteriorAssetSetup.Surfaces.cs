using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static partial class MothersHouseInteriorAssetSetup
    {
        public const string SurfaceAtlasPath = "Assets/Resources/MothersHouse/Textures/MothersHouseSurfaceAtlas.png";
        public const string SurfaceResponsePath = "Assets/Resources/MothersHouse/Textures/MothersHouseMaterialResponse.png";
        public const string SharedSurfaceMaterialPath = "Assets/Resources/MothersHouse/Materials/MothersHouseSurfaceLit.mat";

        public static bool IsSurfaceAtlasPath(string path) => string.Equals(path, SurfaceAtlasPath, StringComparison.OrdinalIgnoreCase);
        public static bool IsSurfaceResponsePath(string path) => string.Equals(path, SurfaceResponsePath, StringComparison.OrdinalIgnoreCase);
        public static bool IsTexturePath(string path) => IsPositiveAtlasPath(path) || IsSurfaceAtlasPath(path) || IsSurfaceResponsePath(path);

        /// <summary>Read-only source/import/UV contract; never reads CPU texture pixels or repairs imports.</summary>
        public static MothersHouseInteriorSurfaceContract ValidateSurfaceTexturesOrThrow() =>
            CreateSurfaceContract(LoadAndValidateManifest());

        private static MothersHouseInteriorSurfaceContract CreateSurfaceContract(MothersHouseManifest manifest)
        {
            MothersHouseSurfaceManifest surface = manifest.surface_contract;
            string[] channels = { "height_slope_u", "height_slope_v", "roughness", "cavity" };
            if (surface == null || surface.version != 1 || surface.design_id != "mothers_house_unique_surface_v1" ||
                surface.albedo != "Textures/MothersHouseSurfaceAtlas" ||
                surface.response != "Textures/MothersHouseMaterialResponse" ||
                surface.albedo_size != MothersHouseInteriorSurfaceContract.AlbedoSize ||
                surface.response_size != MothersHouseInteriorSurfaceContract.ResponseSize ||
                !surface.mipmaps || !surface.srgb_albedo || surface.srgb_response ||
                surface.texture_bytes_budget != MothersHouseInteriorSurfaceContract.TextureBytesBudget ||
                surface.channels == null || !surface.channels.SequenceEqual(channels) ||
                surface.unique_part_count != manifest.parts.Count(part => part.surface_unique_uv))
                throw new InvalidOperationException("The mother's house unique surface manifest contract drifted.");
            foreach (MothersHouseManifestPart part in manifest.parts)
            {
                bool expected = !part.emissive && part.sheet != "Glass" && part.sheet != "Fire";
                if (part.surface_unique_uv != expected)
                    throw new InvalidOperationException($"Part '{part.name}' declares the wrong unique/auxiliary UV ownership.");
            }
            RequireSourceHash(SurfaceAtlasPath, surface.albedo_sha256);
            RequireSourceHash(SurfaceResponsePath, surface.response_sha256);
            RequireSourceHash(PositiveAtlasPath, surface.auxiliary_sha256);
            RequireSourceHash(ModelPath, surface.model_sha256);
            Texture2D albedo = ValidateImportedTexture(SurfaceAtlasPath, surface.albedo_size, true, FilterMode.Trilinear, false);
            Texture2D response = ValidateImportedTexture(SurfaceResponsePath, surface.response_size, false, FilterMode.Trilinear, true);
            Texture2D auxiliary = ValidatePositiveAtlasOrThrow();
            var contract = new MothersHouseInteriorSurfaceContract(albedo, response, auxiliary, surface.uv_signature,
                surface.albedo_sha256, surface.response_sha256, surface.model_sha256, surface.auxiliary_sha256, surface.unique_part_count);
            if (!contract.IsConfigured)
                throw new InvalidOperationException("The house BC7 mip chains/UV signature exceed or invalidate their texture contract.");
            return contract;
        }

        private static Texture2D ValidateImportedTexture(string path, int size, bool sRgb, FilterMode filter, bool alpha)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            int mips = 1;
            for (int dimension = size; dimension > 1; dimension /= 2) mips++;
            if (texture == null || importer == null || texture.width != size || texture.height != size ||
                texture.mipmapCount != mips || texture.format != TextureFormat.BC7 || texture.isReadable ||
                texture.wrapMode != TextureWrapMode.Clamp || texture.filterMode != filter)
                throw new InvalidOperationException($"House texture '{path}' must import at {size} square with complete BC7 mips, clamp/{filter}, no CPU copy.");
            TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings("Standalone");
            if (importer.textureType != TextureImporterType.Default || importer.textureShape != TextureImporterShape.Texture2D ||
                importer.sRGBTexture != sRgb || importer.alphaSource != (alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None) ||
                !importer.mipmapEnabled || importer.isReadable || importer.alphaIsTransparency ||
                importer.wrapMode != TextureWrapMode.Clamp || importer.filterMode != filter ||
                importer.textureCompression != TextureImporterCompression.CompressedHQ || importer.crunchedCompression ||
                importer.npotScale != TextureImporterNPOTScale.None || importer.maxTextureSize != size ||
                !platform.overridden || platform.format != TextureImporterFormat.BC7 || platform.maxTextureSize != size)
                throw new InvalidOperationException($"House texture '{path}' importer must retain explicit PC BC7, mips, source colour space and response alpha.");
            return texture;
        }

        private static void RequireSourceHash(string path, string expected)
        {
            if (string.IsNullOrWhiteSpace(expected) || !File.Exists(path))
                throw new InvalidOperationException($"Missing source/hash for '{path}'. Regenerate the house surface contract.");
            using (var stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    throw new InvalidOperationException($"House source '{path}' differs from its authored SHA256. Regenerate the matching model/texture manifest.");
            }
        }

        private static void ValidateSurfaceRegistry(MothersHouseInteriorAssetRegistry registry, MothersHouseManifest manifest,
            ICollection<string> problems)
        {
            MothersHouseInteriorSurfaceContract expected = CreateSurfaceContract(manifest);
            MothersHouseInteriorSurfaceContract actual = registry.SurfaceContract;
            if (actual == null || !actual.IsConfigured || actual.Albedo != expected.Albedo || actual.Response != expected.Response ||
                actual.UVSignature != expected.UVSignature || actual.AlbedoSha256 != expected.AlbedoSha256 ||
                actual.ResponseSha256 != expected.ResponseSha256 || actual.ModelSha256 != expected.ModelSha256 ||
                actual.Auxiliary != registry.PositiveAtlas.Texture || actual.AuxiliarySha256 != expected.AuxiliarySha256 ||
                actual.UniquePartCount != expected.UniquePartCount)
                problems.Add("registry unique surface textures/UV/source hashes drifted");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(SharedSurfaceMaterialPath);
            if (material == null || material.shader == null || material.shader.name != MothersHouseInteriorSurfaceContract.ShaderName ||
                material.GetTexture("_BaseMap") != expected.Albedo || material.GetTexture("_SceneMaterialResponse") != expected.Response ||
                !Mathf.Approximately(material.GetFloat("_SceneResponseStrength"), 1f) ||
                !Mathf.Approximately(material.GetFloat("_SceneNormalStrength"), .65f))
                problems.Add("shared unique surface material lost its shader, aligned texture inputs or physical response");
        }

        private static Material EnsureSharedSurfaceMaterial()
        {
            Shader shader = Shader.Find(MothersHouseInteriorSurfaceContract.ShaderName);
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("The mother's house physical surface shader is missing or unsupported.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(SharedSurfaceMaterialPath);
            if (material == null)
            {
                Material basis = AssetDatabase.LoadAssetAtPath<Material>(SharedLitMaterialPath);
                if (basis == null) throw new InvalidOperationException("Missing shared Lit material for house surfaces.");
                material = new Material(basis) { name = "Mother's House Shared Surfaces", shader = shader };
                EnsureFolderForAsset(SharedSurfaceMaterialPath);
                AssetDatabase.CreateAsset(material, SharedSurfaceMaterialPath);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(SurfaceAtlasPath));
            material.SetTexture("_SceneMaterialResponse", AssetDatabase.LoadAssetAtPath<Texture2D>(SurfaceResponsePath));
            material.SetTextureScale("_BaseMap", Vector2.one); material.SetTextureOffset("_BaseMap", Vector2.zero);
            material.SetColor("_BaseColor", Color.white); material.SetColor("_Color", Color.white);
            material.SetFloat("_SceneResponseStrength", 1f); material.SetFloat("_SceneNormalStrength", .65f);
            EditorUtility.SetDirty(material);
            return material;
        }

        [Serializable]
        private sealed class MothersHouseSurfaceManifest
        {
            public int version, albedo_size, response_size, unique_part_count;
            public long texture_bytes_budget;
            public string design_id, albedo, response, uv_signature, albedo_sha256, response_sha256,
                auxiliary_sha256, model_sha256;
            public bool mipmaps, srgb_albedo, srgb_response;
            public string[] channels;
        }
    }
}
