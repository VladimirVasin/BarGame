using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Validates and packages the single full-colour V2 clothing atlas. UV0 is
    /// authored directly into atlas subrects, so one shared material is enough
    /// and no runtime texture switching is introduced.
    /// </summary>
    internal static class Player3DV2StaticTextureContract
    {
        public const string AssetPath =
            "Assets/Player3D/V2/Textures/PlayerClothingAtlas.png";
        public const string MaterialPath =
            "Assets/Player3D/V2/Materials/Player3DV2Clothing.mat";
        public const int Width = 256;
        public const int Height = 256;
        public const int UvSafeInsetPixels = 1;

        private static readonly string[] ExpectedMaterials =
        {
            "MAT_JacketAtlas",
            "MAT_JeansAtlas",
            "MAT_BandageAtlas"
        };

        private static readonly ISet<string> TexturedMaterials =
            new HashSet<string>(ExpectedMaterials, StringComparer.Ordinal);

        private static readonly IReadOnlyDictionary<string, ISet<string>>
            ExpectedRenderersByMaterial =
                new Dictionary<string, ISet<string>>(StringComparer.Ordinal)
                {
                    {
                        "MAT_JacketAtlas",
                        new HashSet<string>(StringComparer.Ordinal)
                        {
                            "CLO_JacketBody",
                            "CLO_JacketSleeve.L",
                            "CLO_JacketSleeve.R",
                            "CLO_JacketForearm.R"
                        }
                    },
                    {
                        "MAT_JeansAtlas",
                        new HashSet<string>(StringComparer.Ordinal)
                        {
                            "GEO_Pelvis",
                            "GEO_Thigh.L",
                            "GEO_Shin.L",
                            "GEO_Foot.L",
                            "GEO_Thigh.R",
                            "GEO_Shin.R",
                            "GEO_Foot.R"
                        }
                    },
                    {
                        "MAT_BandageAtlas",
                        new HashSet<string>(StringComparer.Ordinal)
                        {
                            "CLO_Bandage.L"
                        }
                    }
                };

        private static readonly ISet<string> ForbiddenDetailMeshes =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "CLO_ShirtFront",
                "CLO_ShoulderCap.L", "CLO_ShoulderCap.R",
                "CLO_JacketCuff.L", "CLO_JacketCuff.R",
                "CLO_JacketPanel.L", "CLO_JacketPanel.R",
                "ACC_JacketPocket.L", "ACC_JacketPocket.R",
                "ACC_JacketPocketFlap.L", "ACC_JacketPocketFlap.R",
                "ACC_JeansCuff.L", "ACC_JeansCuff.R",
                "ACC_BootSole.L", "ACC_BootSole.R",
                "CLO_Lapel.L", "CLO_Lapel.R",
                "CLO_CollarBack", "CLO_CollarFront.L", "CLO_CollarFront.R",
                "ACC_ShoulderPatch.R",
                "ACC_StrapFront", "ACC_StrapBack", "ACC_StrapShoulder",
                "ACC_StrapBuckle"
            };

        public static bool UsesClothingAtlas(string materialName)
        {
            return materialName != null &&
                   TexturedMaterials.Contains(materialName);
        }

        public static Player3DV2ManifestTextureBinding ValidateManifest(
            Player3DV2ManifestTextureBinding[] bindings,
            IReadOnlyDictionary<string, string> partMaterials)
        {
            if (bindings == null || bindings.Length != 1 || bindings[0] == null)
            {
                throw new InvalidOperationException(
                    "Hero V2 must declare exactly one static texture_binding.");
            }

            Player3DV2ManifestTextureBinding binding = bindings[0];
            if (binding.texture_asset != AssetPath ||
                binding.width_px != Width ||
                binding.height_px != Height ||
                binding.shader_property != "_BaseMap" ||
                binding.color_space != "sRGB" ||
                binding.filter_mode != "Point" ||
                binding.wrap_mode != "Clamp" ||
                binding.mipmaps ||
                binding.compression != "Uncompressed" ||
                binding.uv_channel != 0 ||
                binding.uv_origin != "bottom_left" ||
                binding.uv_safe_inset_px != UvSafeInsetPixels ||
                binding.material_tint_hex != "FFFFFF")
            {
                throw new InvalidOperationException(
                    "Hero V2 clothing texture settings differ from the " +
                    "canonical full-colour _BaseMap contract.");
            }

            ValidateMaterials(binding.materials);
            ValidateSha256(binding.sha256);
            ValidateRendererContract(partMaterials);
            ValidateRegions(binding, partMaterials);
            return binding;
        }

        public static void ValidateTexture(Texture2D texture)
        {
            if (texture == null ||
                texture.width != Width ||
                texture.height != Height)
            {
                throw new InvalidOperationException(
                    $"Hero V2 clothing atlas must import as {Width}x{Height}, " +
                    $"not {texture?.width ?? 0}x{texture?.height ?? 0}.");
            }
        }

        public static void ValidateRendererUvs(
            Player3DV2ManifestTextureBinding binding,
            IReadOnlyDictionary<string, Renderer> renderers)
        {
            for (int regionIndex = 0;
                 regionIndex < binding.regions.Length;
                 regionIndex++)
            {
                Player3DV2ManifestTextureRegion region =
                    binding.regions[regionIndex];
                if (!renderers.TryGetValue(region.renderer, out Renderer renderer))
                {
                    throw new InvalidOperationException(
                        $"Clothing atlas region '{region.name}' references " +
                        $"missing renderer '{region.renderer}'.");
                }

                Mesh mesh = GetRendererMesh(renderer);
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"Textured renderer '{region.renderer}' has no mesh.");
                }

                Vector2[] uv = mesh.uv;
                if (uv == null || uv.Length != mesh.vertexCount || uv.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Textured renderer '{region.renderer}' must have UV0.");
                }

                Vector2 minimum = new Vector2(
                    (float)(region.x_px + binding.uv_safe_inset_px) /
                    binding.width_px,
                    (float)(region.y_px + binding.uv_safe_inset_px) /
                    binding.height_px);
                Vector2 maximum = new Vector2(
                    (float)(region.x_px + region.width_px -
                            binding.uv_safe_inset_px) /
                    binding.width_px,
                    (float)(region.y_px + region.height_px -
                            binding.uv_safe_inset_px) /
                    binding.height_px);
                Vector2 observedMinimum = uv[0];
                Vector2 observedMaximum = uv[0];
                for (int uvIndex = 0; uvIndex < uv.Length; uvIndex++)
                {
                    Vector2 point = uv[uvIndex];
                    if (point.x < minimum.x - 0.0001f ||
                        point.x > maximum.x + 0.0001f ||
                        point.y < minimum.y - 0.0001f ||
                        point.y > maximum.y + 0.0001f)
                    {
                        throw new InvalidOperationException(
                            $"Renderer '{region.renderer}' UV0[{uvIndex}]={point} " +
                            $"lies outside region '{region.name}' " +
                            $"{minimum}..{maximum}.");
                    }

                    observedMinimum = Vector2.Min(observedMinimum, point);
                    observedMaximum = Vector2.Max(observedMaximum, point);
                }

                if (observedMaximum.x - observedMinimum.x <= 0.0001f ||
                    observedMaximum.y - observedMinimum.y <= 0.0001f)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{region.renderer}' has degenerate atlas UV0.");
                }
            }
        }

        public static Material EnsureSharedMaterial(
            Material productionMaterial,
            Texture2D atlas)
        {
            if (productionMaterial == null)
            {
                throw new ArgumentNullException(nameof(productionMaterial));
            }

            if (productionMaterial.shader == null ||
                !productionMaterial.HasProperty("_BaseMap") ||
                !productionMaterial.HasProperty("_BaseColor"))
            {
                throw new InvalidOperationException(
                    "The Hero V2 production shader must expose _BaseMap and " +
                    "_BaseColor for the full-colour clothing contract.");
            }

            ValidateTexture(atlas);
            EnsureFolderForAsset(MaterialPath);
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(productionMaterial)
                {
                    name = "Player3DV2Clothing"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.CopyPropertiesFromMaterial(productionMaterial);
                material.shader = productionMaterial.shader;
            }

            material.color = Color.white;
            SetColorIfPresent(material, "_BaseColor", Color.white);
            SetTextureIfPresent(material, "_BaseMap", atlas);
            SetTextureIfPresent(material, "_MainTex", atlas);
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTextureScale("_BaseMap", Vector2.one);
                material.SetTextureOffset("_BaseMap", Vector2.zero);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTextureScale("_MainTex", Vector2.one);
                material.SetTextureOffset("_MainTex", Vector2.zero);
            }

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        public static bool IsSharedMaterialCanonical(
            Material material,
            Material productionMaterial,
            Texture2D atlas)
        {
            return material != null &&
                   productionMaterial != null &&
                   atlas != null &&
                   material != productionMaterial &&
                   material.shader == productionMaterial.shader &&
                   material.HasProperty("_BaseMap") &&
                   material.HasProperty("_BaseColor") &&
                   material.GetTexture("_BaseMap") == atlas &&
                   material.GetTextureScale("_BaseMap") == Vector2.one &&
                   material.GetTextureOffset("_BaseMap") == Vector2.zero &&
                   material.GetColor("_BaseColor") == Color.white &&
                   material.enableInstancing;
        }

        private static void ValidateMaterials(string[] materials)
        {
            if (materials == null || materials.Length != ExpectedMaterials.Length)
            {
                throw new InvalidOperationException(
                    $"Hero V2 clothing binding must list exactly " +
                    $"{ExpectedMaterials.Length} semantic materials.");
            }

            for (int index = 0; index < ExpectedMaterials.Length; index++)
            {
                if (materials[index] != ExpectedMaterials[index])
                {
                    throw new InvalidOperationException(
                        $"Hero V2 clothing material {index} is " +
                        $"'{materials[index]}'; expected " +
                        $"'{ExpectedMaterials[index]}'.");
                }
            }
        }

        private static void ValidateSha256(string declaredHash)
        {
            if (string.IsNullOrEmpty(declaredHash) || declaredHash.Length != 64)
            {
                throw new InvalidOperationException(
                    "Hero V2 clothing atlas must publish a SHA-256 hash.");
            }

            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(AssetPath);
            string actualHash = BitConverter
                .ToString(sha.ComputeHash(stream))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
            if (!string.Equals(
                    actualHash,
                    declaredHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Hero V2 clothing atlas SHA-256 differs from manifest.");
            }
        }

        private static void ValidateRendererContract(
            IReadOnlyDictionary<string, string> partMaterials)
        {
            foreach (KeyValuePair<string, ISet<string>> expected in
                     ExpectedRenderersByMaterial)
            {
                HashSet<string> actual =
                    new HashSet<string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> part in partMaterials)
                {
                    if (part.Value == expected.Key)
                    {
                        actual.Add(part.Key);
                    }
                }

                if (!actual.SetEquals(expected.Value))
                {
                    throw new InvalidOperationException(
                        $"Hero V2 renderers using {expected.Key} differ from " +
                        "the canonical texture-authored silhouette contract.");
                }
            }

            foreach (string partName in partMaterials.Keys)
            {
                if (ForbiddenDetailMeshes.Contains(partName) ||
                    partName.StartsWith(
                        "ACC_BandageWrap.",
                        StringComparison.Ordinal) ||
                    partName.StartsWith("ACC_Strap", StringComparison.Ordinal) ||
                    partName.IndexOf("Buckle", StringComparison.Ordinal) >= 0)
                {
                    throw new InvalidOperationException(
                        $"Hero V2 obsolete detail mesh '{partName}' must be " +
                        "painted into the clothing atlas.");
                }
            }
        }

        private static void ValidateRegions(
            Player3DV2ManifestTextureBinding binding,
            IReadOnlyDictionary<string, string> partMaterials)
        {
            if (binding.regions == null || binding.regions.Length == 0)
            {
                throw new InvalidOperationException(
                    "Hero V2 clothing binding must publish renderer regions.");
            }

            HashSet<string> regionNames =
                new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> regionRenderers =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < binding.regions.Length; index++)
            {
                Player3DV2ManifestTextureRegion region = binding.regions[index];
                if (region == null ||
                    string.IsNullOrWhiteSpace(region.name) ||
                    string.IsNullOrWhiteSpace(region.renderer) ||
                    !regionNames.Add(region.name) ||
                    !regionRenderers.Add(region.renderer) ||
                    region.x_px < 0 ||
                    region.y_px < 0 ||
                    region.width_px <= binding.uv_safe_inset_px * 2 ||
                    region.height_px <= binding.uv_safe_inset_px * 2 ||
                    region.x_px + region.width_px > binding.width_px ||
                    region.y_px + region.height_px > binding.height_px)
                {
                    throw new InvalidOperationException(
                        $"Hero V2 clothing region {index} is invalid or " +
                        "duplicates a name/renderer.");
                }

                if (!partMaterials.TryGetValue(
                        region.renderer,
                        out string material) ||
                    !UsesClothingAtlas(material))
                {
                    throw new InvalidOperationException(
                        $"Clothing region '{region.name}' renderer " +
                        $"'{region.renderer}' is not a textured manifest part.");
                }

                for (int previousIndex = 0;
                     previousIndex < index;
                     previousIndex++)
                {
                    Player3DV2ManifestTextureRegion previous =
                        binding.regions[previousIndex];
                    bool overlaps =
                        region.x_px < previous.x_px + previous.width_px &&
                        previous.x_px < region.x_px + region.width_px &&
                        region.y_px < previous.y_px + previous.height_px &&
                        previous.y_px < region.y_px + region.height_px;
                    if (overlaps)
                    {
                        throw new InvalidOperationException(
                            $"Clothing atlas regions '{previous.name}' and " +
                            $"'{region.name}' overlap.");
                    }
                }
            }

            foreach (KeyValuePair<string, string> part in partMaterials)
            {
                if (UsesClothingAtlas(part.Value) &&
                    !regionRenderers.Contains(part.Key))
                {
                    throw new InvalidOperationException(
                        $"Textured part '{part.Key}' has no clothing atlas region.");
                }
            }
        }

        private static Mesh GetRendererMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private static void SetColorIfPresent(
            Material material,
            string property,
            Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private static void SetTextureIfPresent(
            Material material,
            string property,
            Texture value)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, value);
            }
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
    }

    [Serializable]
    internal sealed class Player3DV2ManifestTextureBinding
    {
        public string texture_asset;
        public int width_px;
        public int height_px;
        public string[] materials;
        public string shader_property;
        public string color_space;
        public string filter_mode;
        public string wrap_mode;
        public bool mipmaps;
        public string compression;
        public int uv_channel;
        public string uv_origin;
        public int uv_safe_inset_px;
        public string material_tint_hex;
        public string sha256;
        public Player3DV2ManifestTextureRegion[] regions;
    }

    [Serializable]
    internal sealed class Player3DV2ManifestTextureRegion
    {
        public string name;
        public string renderer;
        public int x_px;
        public int y_px;
        public int width_px;
        public int height_px;
    }
}
