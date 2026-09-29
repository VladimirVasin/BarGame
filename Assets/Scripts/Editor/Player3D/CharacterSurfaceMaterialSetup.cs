using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public enum CharacterSurfaceRecipe { Clothing, Skin, Hair }

    /// <summary>A reusable UV0 surface set; base colour remains owned by the character.</summary>
    [Serializable]
    public sealed class CharacterSurfaceBinding
    {
        public string recipe, normal_asset, response_asset, normal_sha256, response_sha256;
        public string uv_origin, base_color_mode;
        public int width_px, height_px, uv_channel;
        public float normal_scale;
        public CharacterSurfaceRegion[] regions;
    }

    [Serializable]
    public sealed class CharacterSurfaceRegion
    {
        public string name, renderer;
        public int x_px, y_px, width_px, height_px;
    }

    /// <summary>
    /// Shared authoring contract for PS1 Lit characters. Linear response RGBA:
    /// R metallic, G/B authoring masks, A absolute smoothness. No character paths.
    /// </summary>
    public static class CharacterSurfaceMaterialSetup
    {
        public static float NormalScale(CharacterSurfaceRecipe recipe) => recipe switch
        {
            CharacterSurfaceRecipe.Clothing => .65f,
            CharacterSurfaceRecipe.Skin => .35f,
            CharacterSurfaceRecipe.Hair => .5f,
            _ => throw new ArgumentOutOfRangeException(nameof(recipe))
        };

        public static void ConfigureImport(TextureImporter importer, bool normal, int maxSize)
        {
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.convertToNormalmap = false;
            importer.flipGreenChannel = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 1;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = false;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var platform = importer.GetPlatformTextureSettings("Standalone");
            platform.overridden = true;
            platform.maxTextureSize = maxSize;
            platform.format = TextureImporterFormat.Automatic;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            platform.crunchedCompression = false;
            importer.SetPlatformTextureSettings(platform);
        }

        public static void Apply(Material material, CharacterSurfaceRecipe recipe,
            Texture2D normal, Texture2D response)
        {
            if (material == null || material.shader == null || material.shader.name != "Bar Promenade/PS1 Lit" ||
                normal == null || response == null)
                throw new InvalidOperationException("Character surfaces require PS1 Lit and both companion maps.");
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", NormalScale(recipe));
            material.SetTexture("_MetallicGlossMap", response);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_WorkflowMode", 1f);
            material.SetFloat("_SpecularHighlights", 1f);
            material.SetFloat("_EnvironmentReflections", 0f);
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            material.DisableKeyword("_SPECULAR_SETUP");
            material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }

        public static Material EnsureSharedMaterial(string assetPath, Material template,
            CharacterSurfaceRecipe recipe, Texture2D normal, Texture2D response, Texture2D baseColor = null)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(template) { name = Path.GetFileNameWithoutExtension(assetPath) };
                AssetDatabase.CreateAsset(material, assetPath);
            }
            material.shader = template.shader;
            material.CopyPropertiesFromMaterial(template);
            material.enableInstancing = template.enableInstancing;
            foreach (string property in new[] { "_BaseMap", "_MainTex" })
            {
                material.SetTexture(property, baseColor);
                material.SetTextureScale(property, Vector2.one);
                material.SetTextureOffset(property, Vector2.zero);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_Color", Color.white);
            Apply(material, recipe, normal, response);
            EditorUtility.SetDirty(material);
            return material;
        }

        public static bool IsCanonical(Material material, CharacterSurfaceRecipe recipe,
            Texture2D normal, Texture2D response) =>
            material != null && material.shader != null && material.shader.name == "Bar Promenade/PS1 Lit" &&
            normal != null && response != null &&
            material.GetTexture("_BumpMap") == normal &&
            material.GetTexture("_MetallicGlossMap") == response &&
            material.GetFloat("_BumpScale") == NormalScale(recipe) &&
            material.GetFloat("_Smoothness") == 1f && material.GetFloat("_Metallic") == 0f &&
            material.GetFloat("_SmoothnessTextureChannel") == 0f && material.GetFloat("_WorkflowMode") == 1f &&
            material.GetFloat("_SpecularHighlights") == 1f && material.GetFloat("_EnvironmentReflections") == 0f &&
            material.IsKeywordEnabled("_NORMALMAP") && material.IsKeywordEnabled("_METALLICSPECGLOSSMAP") &&
            !material.IsKeywordEnabled("_SPECULAR_SETUP") && !material.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A") &&
            !material.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF") && material.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF");

        public static Texture2D LoadMap(string path, bool normal, int width, int height)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (texture == null || texture.width != width || texture.height != height || importer == null ||
                importer.textureType != (normal ? TextureImporterType.NormalMap : TextureImporterType.Default) ||
                importer.sRGBTexture || importer.flipGreenChannel || !importer.mipmapEnabled ||
                importer.filterMode != FilterMode.Trilinear || importer.wrapMode != TextureWrapMode.Clamp)
                throw new InvalidOperationException("Missing or incorrectly imported character surface: " + path);
            return texture;
        }

        public static void ValidateBinding(CharacterSurfaceBinding binding)
        {
            if (binding == null || !Enum.TryParse(binding.recipe, out CharacterSurfaceRecipe recipe) ||
                binding.normal_scale != NormalScale(recipe) || binding.uv_channel != 0 ||
                binding.uv_origin != "bottom_left" || binding.width_px < 1 || binding.height_px < 1 ||
                binding.regions == null || binding.regions.Length == 0)
                throw new InvalidOperationException("Incomplete character surface binding.");
            ValidateHash(binding.normal_asset, binding.normal_sha256);
            ValidateHash(binding.response_asset, binding.response_sha256);
            LoadMap(binding.normal_asset, true, binding.width_px, binding.height_px);
            LoadMap(binding.response_asset, false, binding.width_px, binding.height_px);
            var renderers = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterSurfaceRegion region in binding.regions)
                if (region == null || string.IsNullOrEmpty(region.name) || string.IsNullOrEmpty(region.renderer) ||
                    !renderers.Add(region.renderer) || region.x_px < 0 || region.y_px < 0 ||
                    region.width_px < 1 || region.height_px < 1 ||
                    region.x_px + region.width_px > binding.width_px || region.y_px + region.height_px > binding.height_px)
                    throw new InvalidOperationException("Invalid or duplicate character surface region.");
        }

        public static void ValidateRendererUvs(CharacterSurfaceBinding binding, IReadOnlyDictionary<string, Renderer> renderers)
        {
            foreach (CharacterSurfaceRegion region in binding.regions)
            {
                if (!renderers.TryGetValue(region.renderer, out Renderer renderer))
                    throw new InvalidOperationException("Missing surface renderer: " + region.renderer);
                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || mesh.uv.Length != mesh.vertexCount || mesh.tangents.Length != mesh.vertexCount)
                    throw new InvalidOperationException("Surface requires complete UV0 and tangents: " + region.renderer);
                Vector2 min = new Vector2((float)region.x_px / binding.width_px, (float)region.y_px / binding.height_px);
                Vector2 max = min + new Vector2((float)region.width_px / binding.width_px, (float)region.height_px / binding.height_px);
                foreach (Vector2 uv in mesh.uv)
                    if (!float.IsFinite(uv.x) || !float.IsFinite(uv.y) || uv.x < min.x - .0001f || uv.y < min.y - .0001f ||
                        uv.x > max.x + .0001f || uv.y > max.y + .0001f)
                        throw new InvalidOperationException("Surface UV outside declared region: " + region.renderer);
            }
        }

        private static void ValidateHash(string path, string expected)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !File.Exists(path))
                throw new InvalidOperationException("Missing character surface asset: " + path);
            using var hash = SHA256.Create();
            string actual = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            if (actual != expected) throw new InvalidOperationException("Character surface source hash changed: " + path);
        }
    }
}
