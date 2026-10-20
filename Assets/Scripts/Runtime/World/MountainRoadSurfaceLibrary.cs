using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Road-owned maps and shared materials; callers retain geometry, metre UVs and live MPB state.</summary>
    internal static class MountainRoadSurfaceLibrary
    {
        internal const string ResourceFolder = "MountainRoad/Surfaces/";
        internal const string ManifestResourcePath = ResourceFolder + "MountainRoadSurfaceTextures";
        internal const string ShaderName = "Bar Promenade/Mountain Road Surface";
        internal const string FoliageShaderName = ShaderName + " Foliage";
        internal const long TextureBytesBudget = 80L * 1024L * 1024L;
        internal static readonly string[] Families = Enum.GetNames(typeof(MountainRoadSurfaceKind));
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        private static MountainRoadSurfaceManifest manifest;
        private static Material sharedMaterial, foliageMaterial;

        internal static MountainRoadSurfaceManifest Manifest
        {
            get
            {
                if (manifest != null) return manifest;
                TextAsset text = Resources.Load<TextAsset>(ManifestResourcePath);
                if (text == null) throw new InvalidOperationException("Missing road surface manifest.");
                return manifest = ParseManifestOrThrow(text.text);
            }
        }

        internal static MountainRoadSurfaceManifest ParseManifestOrThrow(string json)
        {
            MountainRoadSurfaceManifest data = JsonUtility.FromJson<MountainRoadSurfaceManifest>(json);
            if (data == null || data.design_id != "mountain_road_material_surfaces_v1" || data.generator_version != "1.0.0" ||
                data.gpu_budget_bytes != TextureBytesBudget || data.estimated_gpu_bytes <= 0 || data.estimated_gpu_bytes > TextureBytesBudget ||
                data.sheets == null || data.sheets.Length != Families.Length || data.additional_maps == null)
                throw new InvalidOperationException("Invalid road surface manifest/budget.");
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (MountainRoadSurfaceSheet sheet in data.sheets)
                if (sheet == null || Array.IndexOf(Families, sheet.family) < 0 || !found.Add(sheet.family) ||
                    sheet.albedo != ResourceFolder + sheet.family + "Albedo" || sheet.response != ResourceFolder + sheet.family + "Response" ||
                    !Hash(sheet.albedo_sha256) || !Hash(sheet.response_sha256) || sheet.size < 1024 || sheet.size > 2048 ||
                    sheet.response_size != sheet.size / 2 || !Positive(sheet.meters_per_tile) ||
                    !Positive(sheet.mean_linear_luminance) || sheet.mean_linear_luminance > 1f ||
                    (!string.IsNullOrEmpty(sheet.ground_response) &&
                        (sheet.ground_response != ResourceFolder + sheet.family + "GroundResponse" || !Hash(sheet.ground_response_sha256))))
                    throw new InvalidOperationException("Road surface family contract drifted: " + sheet?.family);
            return data;
        }

        private static bool Positive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Hash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        internal static MountainRoadSurfaceSheet GetSheet(string family)
        {
            foreach (MountainRoadSurfaceSheet sheet in Manifest.sheets) if (sheet.family == family) return sheet;
            throw new InvalidOperationException("Unknown road surface family: " + family);
        }
        internal static MountainRoadSurfaceSheet GetSheet(MountainRoadSurfaceKind kind) => GetSheet(kind.ToString());
        internal static Texture2D GetTexture(MountainRoadSurfaceKind kind) => Load(GetSheet(kind).albedo);
        internal static Texture2D GetGroundResponse(MountainRoadSurfaceKind kind)
        {
            string path = GetSheet(kind).ground_response;
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Missing road ground companion: " + kind);
            return Load(path);
        }
        private static Texture2D Load(string path)
        {
            if (!Textures.TryGetValue(path, out Texture2D texture))
            {
                texture = Resources.Load<Texture2D>(path);
                if (texture == null) throw new InvalidOperationException("Missing road surface texture: " + path);
                Textures.Add(path, texture);
            }
            return texture;
        }

        internal static Material SharedMaterial => Shared(ref sharedMaterial, "Shaders/MountainRoadSurfaceLit", "Mountain Road Surfaces (Shared)", RuntimePrimitiveFactory.DefaultMaterial);
        internal static Material FoliageMaterial => Shared(ref foliageMaterial, "Shaders/MountainRoadSurfaceFoliage", "Mountain Road Wind Surfaces (Shared)", MountainRoadSurfaceAppearance.FoliageMaterial);
        private static Material Shared(ref Material material, string shaderPath, string name, Material donor)
        {
            if (material != null) return material;
            Shader shader = Resources.Load<Shader>(shaderPath);
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Missing/unsupported road surface shader: " + shaderPath);
            material = new Material(donor) { name = name, shader = shader, hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            material.SetFloat("_SceneResponseStrength", 1f); material.SetFloat("_SceneNormalStrength", .65f);
            return material;
        }
        internal static bool IsScoped(Renderer renderer) => renderer != null &&
            renderer.name != "Imported Convex Mirror ConvexMirrorFace" &&
            renderer.GetComponentInParent<MountainRoadSurfaceScope>() != null;

        internal static Color CreateDisplayTint(Color tint, MountainRoadSurfaceKind kind)
        {
            float mean = GetSheet(kind).mean_linear_luminance;
            return new Color(Compensate(tint.r, mean), Compensate(tint.g, mean), Compensate(tint.b, mean), tint.a);
        }
        private static float Compensate(float value, float mean) => Mathf.LinearToGammaSpace(Mathf.Clamp01(Mathf.GammaToLinearSpace(value) / mean));

        internal static void Apply(Renderer renderer, MountainRoadSurfaceKind kind, SurfaceProjection projection, Color tint, int index = -1)
        {
            HomeSurfaceRecipe original = MountainRoadSurfaceAppearance.GetRecipe(kind);
            Vector4 uv = SurfaceAppearanceCore.CreateBaseMapTransform(renderer, original.MetersPerTile, .35f, projection, 9000 + (int)kind);
            ApplyMaps(renderer, kind, tint, uv, SharedMaterial, index);
            ApplyGround(renderer, kind, index);
        }
        internal static void ApplyCombined(Renderer renderer, MountainRoadSurfaceKind kind, Color tint, int index = -1)
        {
            ApplyMaps(renderer, kind, tint, new Vector4(1, 1, 0, 0), SharedMaterial, index);
            ApplyGround(renderer, kind, index);
        }
        internal static void ApplyCombined(Renderer renderer, MountainRoadSurfaceKind kind, Color tint, Material original)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            bool ordinary = original == RuntimePrimitiveFactory.DefaultMaterial;
            bool wind = original == MountainRoadSurfaceAppearance.FoliageMaterial;
            ApplyMaps(renderer, kind, tint, new Vector4(1, 1, 0, 0), wind ? FoliageMaterial : ordinary ? SharedMaterial : original, -1);
            if (ordinary) ApplyGround(renderer, kind, -1);
            else if (!wind && original.HasProperty("_GroundResponse") && IsGround(kind, out _))
                GroundSurfaceAppearance.ApplyRidgeResponse(renderer, GetTexture(kind), -1, GetGroundResponse(kind));
        }

        private static void ApplyMaps(Renderer renderer, MountainRoadSurfaceKind kind, Color tint, Vector4 uv, Material material, int index)
        {
            GroundSurfaceAppearance.AssignSharedMaterial(renderer, material, index);
            Read(renderer, index);
            Properties.SetTexture("_BaseMap", GetTexture(kind));
            Properties.SetTexture("_SceneMaterialResponse", Load(GetSheet(kind).response));
            Properties.SetVector("_BaseMap_ST", uv);
            Color display = CreateDisplayTint(tint, kind);
            Properties.SetColor("_BaseColor", display); Properties.SetColor("_Color", display);
            HomeSurfaceRecipe original = MountainRoadSurfaceAppearance.GetRecipe(kind);
            Properties.SetFloat("_Smoothness", original.Smoothness); Properties.SetFloat("_Metallic", original.Metallic);
            Properties.SetFloat("_SceneResponseStrength", 1f); Properties.SetFloat("_SceneNormalStrength", .65f);
            Write(renderer, index);
        }

        internal static void ApplyUnique(Renderer renderer, Texture2D albedo, Texture2D response, float metallic = 0f)
        {
            if (renderer == null || albedo == null || response == null) throw new ArgumentNullException("Unique road material needs its renderer and aligned maps.");
            renderer.sharedMaterial = SharedMaterial;
            Read(renderer, -1);
            Properties.SetTexture("_BaseMap", albedo); Properties.SetTexture("_SceneMaterialResponse", response);
            Properties.SetVector("_BaseMap_ST", new Vector4(1, 1, 0, 0));
            Properties.SetColor("_BaseColor", Color.white); Properties.SetColor("_Color", Color.white);
            Properties.SetFloat("_Metallic", metallic);
            Properties.SetFloat("_SceneResponseStrength", 1f); Properties.SetFloat("_SceneNormalStrength", .65f);
            Write(renderer, -1);
        }

        private static bool IsGround(MountainRoadSurfaceKind kind, out GroundSurfaceKind ground)
        {
            switch (kind)
            {
                case MountainRoadSurfaceKind.Asphalt: ground = GroundSurfaceKind.Asphalt; return true;
                case MountainRoadSurfaceKind.ForestFloor: ground = GroundSurfaceKind.Soil; return true;
                case MountainRoadSurfaceKind.WindSnow: ground = GroundSurfaceKind.Snow; return true;
                case MountainRoadSurfaceKind.LayeredStone: ground = GroundSurfaceKind.Rock; return true;
                case MountainRoadSurfaceKind.Concrete: ground = GroundSurfaceKind.Concrete; return true;
                case MountainRoadSurfaceKind.Masonry: ground = GroundSurfaceKind.Paving; return true;
                case MountainRoadSurfaceKind.Timber: ground = GroundSurfaceKind.Timber; return true;
                default: ground = default; return false;
            }
        }
        private static void ApplyGround(Renderer renderer, MountainRoadSurfaceKind kind, int index)
        {
            if (!IsGround(kind, out GroundSurfaceKind ground)) return;
            GroundSurfaceAppearance.Apply(renderer, ground, GetTexture(kind), index, GetGroundResponse(kind), configureSnowSubstrate: false);
            if (ground == GroundSurfaceKind.Snow)
            {
                Read(renderer, index);
                Properties.SetTexture("_GroundSubstrateMap", GetTexture(MountainRoadSurfaceKind.ForestFloor));
                Properties.SetTexture("_GroundSubstrateResponse", GetGroundResponse(MountainRoadSurfaceKind.ForestFloor));
                Properties.SetTexture("_GroundAsphaltMap", GetTexture(MountainRoadSurfaceKind.Asphalt));
                Properties.SetTexture("_GroundAsphaltResponse", GetGroundResponse(MountainRoadSurfaceKind.Asphalt));
                Properties.SetColor("_GroundAsphaltColor", CreateDisplayTint(AlpineVillageRoadSurfaceBuilder.AsphaltTint, MountainRoadSurfaceKind.Asphalt));
                Properties.SetColor("_GroundSubstrateColor", CreateDisplayTint(new Color(.155f, .175f, .145f), MountainRoadSurfaceKind.ForestFloor));
                Write(renderer, index);
            }
            Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (kind == MountainRoadSurfaceKind.Asphalt && mesh != null && mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord3))
                GroundSurfaceCoordinates.Enable(renderer, index);
        }
        private static void Read(Renderer renderer, int index) => GroundSurfaceAppearance.ReadProperties(renderer, Properties, index);
        private static void Write(Renderer renderer, int index) => GroundSurfaceAppearance.WriteProperties(renderer, Properties, index);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            foreach (Material material in new[] { sharedMaterial, foliageMaterial })
            {
                if (material == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(material); else UnityEngine.Object.DestroyImmediate(material);
            }
            sharedMaterial = foliageMaterial = null; manifest = null; Textures.Clear();
        }
    }

    [Serializable]
    internal sealed class MountainRoadSurfaceManifest
    {
        public string design_id, generator_version;
        public long gpu_budget_bytes, estimated_gpu_bytes;
        public MountainRoadSurfaceSheet[] sheets;
        public MountainRoadSurfaceAdditionalMap[] additional_maps;
    }
    [Serializable]
    internal sealed class MountainRoadSurfaceSheet
    {
        public string family, albedo, response, ground_response, albedo_sha256, response_sha256, ground_response_sha256;
        public int size, response_size;
        public float meters_per_tile, mean_linear_luminance;
        public bool full_color;
    }
    [Serializable]
    internal sealed class MountainRoadSurfaceAdditionalMap
    {
        public string resource, sha256;
        public int size;
        public bool mipmaps, sRGB;
    }
}
