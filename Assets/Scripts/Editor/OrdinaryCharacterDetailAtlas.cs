using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>One palette-neutral surface atlas, shared by the detailed ordinary cast.</summary>
    public static class OrdinaryCharacterDetailAtlas
    {
        public const string AssetPath = "Assets/Pedestrians/Textures/OrdinaryCharacterDetailAtlas.png";

        [Serializable]
        public sealed class Binding
        {
            public string texture_asset, sha256, shader_property, color_space, filter_mode, wrap_mode;
            public string compression, uv_origin, material_tint_hex, tint_source;
            public int width_px, height_px, uv_channel, uv_safe_inset_px;
            public bool mipmaps;
            public string[] materials;
            public Region[] regions;
        }

        [Serializable]
        public sealed class Region
        {
            public string name, renderer;
            public int x_px, y_px, width_px, height_px;
        }

        public static Texture2D LoadOrThrow()
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath);
            if (texture == null || texture.width != 256 || texture.height != 256 ||
                texture.filterMode != FilterMode.Point || texture.wrapMode != TextureWrapMode.Clamp)
                throw new InvalidOperationException("Missing or incorrectly imported ordinary character detail atlas.");
            return texture;
        }

        public static void Import()
        {
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            LoadOrThrow();
        }

        public static void ValidateBinding(Binding binding, IReadOnlyDictionary<string, string> parts)
        {
            if (binding == null || binding.texture_asset != AssetPath || binding.width_px != 256 ||
                binding.height_px != 256 || binding.shader_property != "_BaseMap" || binding.color_space != "sRGB" ||
                binding.filter_mode != "Point" || binding.wrap_mode != "Clamp" || binding.mipmaps ||
                binding.compression != "Uncompressed" || binding.uv_channel != 0 || binding.uv_origin != "bottom_left" ||
                binding.uv_safe_inset_px != 1 || binding.material_tint_hex != "FFFFFF" ||
                binding.tint_source != "renderer_palette" || binding.materials == null || binding.materials.Length != 0 ||
                binding.regions == null || binding.regions.Length == 0)
                throw new InvalidOperationException("Ordinary character detail atlas metadata is invalid.");
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(AssetPath))
                if (!string.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""),
                    binding.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Ordinary character detail atlas hash differs from its manifest.");
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            foreach (Region region in binding.regions)
            {
                if (region == null || region.renderer == "GEO_FaceSurface" ||
                    !assigned.Add(region.renderer) || !parts.TryGetValue(region.renderer, out string name) || name != region.name)
                    throw new InvalidOperationException("Ordinary character atlas renderer mapping is invalid.");
                Vector2Int origin = CellOrigin(region.name);
                if (region.x_px != origin.x || region.y_px != origin.y || region.width_px != 128 || region.height_px != 128)
                    throw new InvalidOperationException("Ordinary character atlas cell is invalid.");
            }
            if (parts.Any(part => !string.IsNullOrEmpty(part.Value) && !assigned.Contains(part.Key)))
                throw new InvalidOperationException("A character surface has no declared atlas region.");
        }

        public static void ValidateUvs(Renderer renderer, string region)
        {
            Vector2Int origin = CellOrigin(region);
            Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || mesh.uv.Length != mesh.vertexCount || mesh.vertexCount == 0)
                throw new InvalidOperationException("Character detail surface has missing UV0: " + renderer.name);
            Vector2 min = new Vector2((origin.x + 1) / 256f, (origin.y + 1) / 256f);
            Vector2 max = new Vector2((origin.x + 127) / 256f, (origin.y + 127) / 256f);
            foreach (Vector2 uv in mesh.uv)
                if (uv.x < min.x - .0001f || uv.y < min.y - .0001f || uv.x > max.x + .0001f || uv.y > max.y + .0001f)
                    throw new InvalidOperationException("Character detail UV escapes its material cell: " + renderer.name);
        }

        private static Vector2Int CellOrigin(string region)
        {
            string kind = region?.Substring(region.LastIndexOf(':') + 1);
            switch (kind)
            {
                case "cloth": return new Vector2Int(0, 128);
                case "leather": return new Vector2Int(128, 128);
                case "hair": return new Vector2Int(128, 0);
                case "skin_white": return Vector2Int.zero;
                default: throw new InvalidOperationException("Unknown ordinary character material cell: " + region);
            }
        }
    }
}
