using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Junction authoring and read-only source/import gate for the village's local surface package.</summary>
    public static class VillageSurfaceTextureSetup
    {
        public const string TextureFolder = "Assets/Resources/" + VillageSurfaceAppearance.ResourceFolder;
        public const string ManifestPath = "Assets/Resources/" + VillageSurfaceAppearance.ManifestResourcePath + ".json";

        [MenuItem("Bar Promenade/Village/Bake Junctions And Validate Physical Surfaces")]
        public static void BuildOrThrow()
        {
            string json = File.ReadAllText(ManifestPath);
            VillageSurfaceManifest data = VillageSurfaceAppearance.ParseManifestOrThrow(json);
            if (data.additional_maps == null) throw new InvalidOperationException("Missing village additional map ownership before the junction bake.");
            var masks = new Dictionary<string, string>(StringComparer.Ordinal);
            AlpineVillagePlan plan = AlpineVillagePlanner.Create(GameSessionState.DefaultCitySeed);
            foreach (AlpineVillageJunctionPlan junction in plan.Expansion.Junctions)
            {
                string path = VillageJunctionTextureSetup.TextureFolder + junction.StableId + "-Mask.png";
                masks.Add(path, SourceHash(path));
            }
            // Existing masks remain the artist's source. The bake samples the
            // same local pigments/ground companions as the surrounding road.
            VillageJunctionTextureSetup.BuildOrThrow();
            foreach (KeyValuePair<string, string> mask in masks) RequireHash(mask.Key, mask.Value);

            int begin = json.IndexOf("\"additional_maps\"", StringComparison.Ordinal);
            begin = begin < 0 ? -1 : json.IndexOf('[', begin);
            int end = FindArrayEnd(json, begin);
            string additional = json.Substring(begin, end - begin);
            int junctionMaps = 0;
            foreach (VillageSurfaceAdditionalMap map in data.additional_maps)
            {
                string path = "Assets/Resources/" + map.resource + ".png";
                if (path != VillageJunctionTextureSetup.AtlasPath && path != VillageJunctionTextureSetup.ResponseAtlasPath) continue;
                junctionMaps++;
                string current = SourceHash(path);
                if (string.Equals(current, map.sha256, StringComparison.Ordinal)) continue;
                int index = additional.IndexOf(map.sha256, StringComparison.Ordinal);
                if (index < 0 || additional.IndexOf(map.sha256, index + map.sha256.Length, StringComparison.Ordinal) >= 0)
                    throw new InvalidOperationException("Junction source hash is missing/ambiguous inside the village additional maps.");
                additional = additional.Substring(0, index) + current + additional.Substring(index + map.sha256.Length);
            }
            if (junctionMaps != 2) throw new InvalidOperationException("The village package must own exactly its two junction atlases.");
            string updated = json.Substring(0, begin) + additional + json.Substring(end);
            if (!string.Equals(json, updated, StringComparison.Ordinal))
                File.WriteAllText(ManifestPath, updated, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            ValidateOrThrow();
        }

        private static int FindArrayEnd(string json, int begin)
        {
            if (begin < 0) throw new InvalidOperationException("Missing village additional maps JSON array.");
            bool quoted = false, escaped = false;
            int depth = 0;
            for (int index = begin; index < json.Length; index++)
            {
                char character = json[index];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (character == '\\') escaped = true;
                    else if (character == '"') quoted = false;
                    continue;
                }
                if (character == '"') quoted = true;
                else if (character == '[') depth++;
                else if (character == ']' && --depth == 0) return index + 1;
            }
            throw new InvalidOperationException("Incomplete village additional maps JSON array.");
        }

        [MenuItem("Bar Promenade/Village/Validate Physical Surface Textures")]
        public static void ValidateOrThrow()
        {
            VillageSurfaceManifest manifest = ReadManifest();
            string uniquePath = TextureFolder + "VillageUniqueSurfaces.json";
            if (!File.Exists(uniquePath)) throw new InvalidOperationException("Missing village unique surface layout.");
            UniqueManifest unique = JsonUtility.FromJson<UniqueManifest>(File.ReadAllText(uniquePath));
            if (unique == null || unique.design_id != "village_unique_physical_surfaces_v1" ||
                unique.albedo != VillageSurfaceAppearance.UniqueAlbedoPath || unique.response != VillageSurfaceAppearance.UniqueResponsePath ||
                unique.albedo_size != 4096 || unique.response_size != 2048 || unique.unique_part_count <= 0 ||
                unique.maps?.VillageUniqueAlbedo == null || unique.maps.VillageUniqueResponse == null)
                throw new InvalidOperationException("Village unique layout/map ownership drifted.");
            var owned = new HashSet<string>(StringComparer.Ordinal);
            long measured = 0;
            foreach (VillageSurfaceSheet sheet in manifest.sheets)
            {
                measured += Validate(sheet.albedo, sheet.size, true, false, TextureWrapMode.Repeat, sheet.albedo_sha256, owned);
                measured += Validate(sheet.response, sheet.response_size, false, true, TextureWrapMode.Repeat, sheet.response_sha256, owned);
                if (!string.IsNullOrEmpty(sheet.ground_response))
                    measured += Validate(sheet.ground_response, 512, false, true, TextureWrapMode.Repeat, sheet.ground_response_sha256, owned);
            }
            measured += Validate(VillageSurfaceAppearance.UniqueAlbedoPath, 4096, true, false, TextureWrapMode.Clamp, unique.maps.VillageUniqueAlbedo.sha256, owned);
            measured += Validate(VillageSurfaceAppearance.UniqueResponsePath, 2048, false, true, TextureWrapMode.Clamp, unique.maps.VillageUniqueResponse.sha256, owned);
            if (manifest.additional_maps == null || manifest.additional_maps.Length != 6)
                throw new InvalidOperationException("Village package must include its unique pair, guarded junctions and existing artwork.");
            var additional = new HashSet<string>(StringComparer.Ordinal);
            foreach (VillageSurfaceAdditionalMap map in manifest.additional_maps)
            {
                if (map == null || !additional.Add(map.resource)) throw new InvalidOperationException("Missing/duplicate village additional map record.");
                string path = "Assets/Resources/" + map.resource + ".png";
                RequireHash(path, map.sha256);
                if (owned.Contains(path)) continue;
                measured += ValidateRetained(map, owned);
            }
            if (measured != manifest.estimated_gpu_bytes || measured > VillageSurfaceAppearance.TextureBytesBudget)
                throw new InvalidOperationException("Village imported physical texture mip chains exceed the recorded package budget.");
            Shader shader = Resources.Load<Shader>(VillageSurfaceAppearance.ShaderPath);
            Shader foliage = Resources.Load<Shader>("Shaders/VillageSurfaceFoliage");
            if (shader == null || !shader.isSupported || shader.name != VillageSurfaceAppearance.ShaderName ||
                foliage == null || !foliage.isSupported || foliage.name != "Bar Promenade/Village Surface Foliage")
                throw new InvalidOperationException("The village's shared physical and wind surface shaders must both be supported.");
        }

        internal static VillageSurfaceManifest ReadManifest()
        {
            if (!File.Exists(ManifestPath)) throw new InvalidOperationException("Generate the village physical surfaces first.");
            return VillageSurfaceAppearance.ParseManifestOrThrow(File.ReadAllText(ManifestPath));
        }

        private static long Validate(string resourcePath, int size, bool sRgb, bool alpha, TextureWrapMode wrap, string sha256, ISet<string> owned)
        {
            string path = "Assets/Resources/" + resourcePath + ".png";
            if (!owned.Add(path)) throw new InvalidOperationException("Village surface source is duplicated: " + path);
            if (sha256 != null) RequireHash(path, sha256);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            int mips = 1;
            for (int dimension = size; dimension > 1; dimension /= 2) mips++;
            if (texture == null || importer == null || texture.width != size || texture.height != size ||
                texture.mipmapCount != mips || texture.format != TextureFormat.BC7 || texture.isReadable ||
                texture.wrapMode != wrap || texture.filterMode != FilterMode.Trilinear)
                throw new InvalidOperationException("Village texture must use complete non-readable BC7 mips: " + path);
            TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings("Standalone");
            if (importer.textureType != TextureImporterType.Default || importer.textureShape != TextureImporterShape.Texture2D ||
                importer.sRGBTexture != sRgb || importer.alphaSource != (alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None) ||
                !importer.mipmapEnabled || importer.streamingMipmaps || importer.isReadable || importer.alphaIsTransparency ||
                !importer.ignoreMipmapLimit ||
                importer.wrapMode != wrap || importer.filterMode != FilterMode.Trilinear || importer.anisoLevel != 4 ||
                importer.textureCompression != TextureImporterCompression.CompressedHQ || importer.crunchedCompression ||
                importer.npotScale != TextureImporterNPOTScale.None || importer.maxTextureSize != size ||
                !platform.overridden || platform.format != TextureImporterFormat.BC7 || platform.maxTextureSize != size)
                throw new InvalidOperationException("Village texture importer lost its measured PC surface contract: " + path);
            long bytes = 0;
            for (int dimension = size; ; dimension = Math.Max(1, dimension / 2))
            {
                long blocks = Math.Max(1, (dimension + 3) / 4);
                bytes += blocks * blocks * 16;
                if (dimension == 1) return bytes;
            }
        }

        private static long ValidateRetained(VillageSurfaceAdditionalMap map, ISet<string> owned)
        {
            string path = "Assets/Resources/" + map.resource + ".png";
            bool junction = path == VillageJunctionTextureSetup.AtlasPath || path == VillageJunctionTextureSetup.ResponseAtlasPath;
            bool picture = path == VillageExpansionAssetSetup.LodgePicturesPath || path == VillageExpansionAssetSetup.LodgeGroupPhotographPath;
            bool groupPhotograph = path == VillageExpansionAssetSetup.LodgeGroupPhotographPath;
            if ((!junction && !picture) || !owned.Add(path) || map.size != (junction || groupPhotograph ? 2048 : 1024) || map.mipmaps != !junction ||
                map.sRGB != (path != VillageJunctionTextureSetup.ResponseAtlasPath))
                throw new InvalidOperationException("Unexpected village retained map ownership/profile: " + path);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Vector2Int sourceSize = ReadPngSize(path);
            Vector2Int projectedSize = groupPhotograph
                ? new Vector2Int(Mathf.NextPowerOfTwo(sourceSize.x), Mathf.NextPowerOfTwo(sourceSize.y)) : sourceSize;
            float scale = Mathf.Min(1f, map.size / (float)Mathf.Max(projectedSize.x, projectedSize.y));
            float expectedWidth = projectedSize.x * scale, expectedHeight = projectedSize.y * scale;
            int mips = 1;
            if (map.mipmaps)
                for (int dimension = Mathf.Min(map.size, Mathf.Max(projectedSize.x, projectedSize.y)); dimension > 1; dimension /= 2) mips++;
            FilterMode filter = junction ? FilterMode.Bilinear : FilterMode.Trilinear;
            // Unity may truncate the minor axis when scaling an NPOT source.
            // Retain the exact major axis and source aspect to one texel;
            // memory below always uses the actual imported block dimensions.
            if (texture == null || importer == null ||
                Mathf.Max(texture.width, texture.height) != Mathf.Min(map.size, Mathf.Max(projectedSize.x, projectedSize.y)) ||
                Mathf.Abs(texture.width - expectedWidth) > 1f || Mathf.Abs(texture.height - expectedHeight) > 1f || texture.mipmapCount != mips ||
                texture.format != TextureFormat.BC7 || texture.isReadable || texture.wrapMode != TextureWrapMode.Clamp || texture.filterMode != filter)
                throw new InvalidOperationException($"Village retained map must retain complete BC7 dimensions/filter: {path}. " +
                    $"Source {sourceSize.x}x{sourceSize.y}, expected aspect {expectedWidth:F3}x{expectedHeight:F3}, max {map.size}, mips {mips}, clamp/{filter}. " +
                    (texture == null ? "Actual texture missing." :
                     $"Actual {texture.width}x{texture.height}, mips {texture.mipmapCount}, format {texture.format}, wrap {texture.wrapMode}, filter {texture.filterMode}, readable {texture.isReadable}, importer present {importer != null}."));
            TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings("Standalone");
            if (importer.textureType != TextureImporterType.Default || importer.textureShape != TextureImporterShape.Texture2D ||
                importer.sRGBTexture != map.sRGB || importer.mipmapEnabled != map.mipmaps || importer.streamingMipmaps || importer.isReadable ||
                importer.alphaIsTransparency || importer.alphaSource != (map.sRGB ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput) ||
                !importer.ignoreMipmapLimit || importer.wrapMode != TextureWrapMode.Clamp || importer.filterMode != filter || importer.anisoLevel != 4 ||
                importer.textureCompression != TextureImporterCompression.CompressedHQ || importer.crunchedCompression ||
                importer.npotScale != (groupPhotograph ? TextureImporterNPOTScale.ToLarger : TextureImporterNPOTScale.None) || importer.maxTextureSize != map.size ||
                !platform.overridden || platform.format != TextureImporterFormat.BC7 || platform.maxTextureSize != map.size)
                throw new InvalidOperationException("Village retained map lost its measured source colour/import contract: " + path);
            long bytes = 0;
            int width = texture.width, height = texture.height;
            while (true)
            {
                bytes += (long)Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16;
                if (!map.mipmaps || width == 1 && height == 1) return bytes;
                width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
            }
        }

        private static Vector2Int ReadPngSize(string path)
        {
            using (Stream stream = File.OpenRead(path))
            {
                var header = new byte[24];
                if (stream.Read(header, 0, header.Length) != header.Length || header[0] != 137 || header[1] != 80 ||
                    header[12] != 73 || header[13] != 72 || header[14] != 68 || header[15] != 82)
                    throw new InvalidOperationException("Village source is not a complete PNG header: " + path);
                int width = header[16] << 24 | header[17] << 16 | header[18] << 8 | header[19];
                int height = header[20] << 24 | header[21] << 16 | header[22] << 8 | header[23];
                if (width <= 0 || height <= 0) throw new InvalidOperationException("Village PNG source has invalid dimensions: " + path);
                return new Vector2Int(width, height);
            }
        }

        internal static void RequireHash(string path, string expected)
        {
            if (string.IsNullOrEmpty(expected) || !string.Equals(SourceHash(path), expected, StringComparison.Ordinal))
                throw new InvalidOperationException("Village surface source differs from its authored SHA256: " + path);
        }

        private static string SourceHash(string path)
        {
            if (!File.Exists(path)) throw new InvalidOperationException("Missing measured village source: " + path);
            using (Stream stream = File.OpenRead(path))
            using (SHA256 hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        internal static void ValidateUniqueMeshOrThrow(Mesh mesh, bool unique, string family, string uv0Signature, string uv1Signature)
        {
            if (!unique) return;
            if (mesh == null || string.IsNullOrEmpty(family) || !IsHash(uv0Signature) || !IsHash(uv1Signature))
                throw new InvalidOperationException("Village close-up lost its measured surface/UV identity: " + mesh?.name);
            Vector2[] packed = mesh.uv, metric = mesh.uv2;
            if (packed.Length != mesh.vertexCount || metric.Length != mesh.vertexCount)
                throw new InvalidOperationException("Village close-up must retain packed UV0 and original metre UV1: " + mesh.name);
            for (int i = 0; i < packed.Length; i++)
                if (!Finite(packed[i]) || !Finite(metric[i]) || packed[i].x <= 0f || packed[i].x >= 1f || packed[i].y <= 0f || packed[i].y >= 1f)
                    throw new InvalidOperationException("Village close-up escaped its padded atlas or lost finite metre coordinates: " + mesh.name);
        }
        private static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y);
        private static bool IsHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char character in value) if (!Uri.IsHexDigit(character)) return false;
            return true;
        }

        [Serializable] private sealed class UniqueManifest
        {
            public string design_id, albedo, response;
            public int albedo_size, response_size, unique_part_count;
            public UniqueMaps maps;
        }
        [Serializable] private sealed class UniqueMaps
        {
            public UniqueMap VillageUniqueAlbedo, VillageUniqueResponse;
        }
        [Serializable] private sealed class UniqueMap { public string sha256; }
    }

    public sealed class VillageSurfaceTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!(assetImporter is TextureImporter importer) || !assetPath.StartsWith(VillageSurfaceTextureSetup.TextureFolder, StringComparison.Ordinal)) return;
            string resource = assetPath.Substring("Assets/Resources/".Length);
            if (!resource.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
            resource = resource.Substring(0, resource.Length - 4);
            bool albedo, unique = false;
            int size;
            if (resource == VillageSurfaceAppearance.UniqueAlbedoPath) { albedo = true; unique = true; size = 4096; }
            else if (resource == VillageSurfaceAppearance.UniqueResponsePath) { albedo = false; unique = true; size = 2048; }
            else
            {
                VillageSurfaceSheet found = null;
                foreach (VillageSurfaceSheet sheet in VillageSurfaceTextureSetup.ReadManifest().sheets)
                    if (resource == sheet.albedo || resource == sheet.response || resource == sheet.ground_response) { found = sheet; break; }
                if (found == null) throw new InvalidOperationException("Unregistered village surface texture: " + resource);
                albedo = resource == found.albedo;
                size = albedo ? found.size : resource == found.response ? found.response_size : 512;
            }
            importer.textureType = TextureImporterType.Default; importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = albedo; importer.alphaSource = albedo ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false; importer.mipmapEnabled = true; importer.streamingMipmaps = false;
            importer.isReadable = false; importer.npotScale = TextureImporterNPOTScale.None;
            importer.ignoreMipmapLimit = true;
            importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 4;
            importer.wrapMode = unique ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.crunchedCompression = false;
            importer.maxTextureSize = size;
            TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings("Standalone");
            platform.name = "Standalone"; platform.overridden = true; platform.format = TextureImporterFormat.BC7;
            platform.maxTextureSize = size; platform.compressionQuality = 100; platform.crunchedCompression = false;
            importer.SetPlatformTextureSettings(platform);
        }
    }
}
