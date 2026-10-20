using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded, scene-local surface library. Geometry retains its metre UVs;
    /// palette owners retain their tints. Shared City/Mountain Road resources are never changed.</summary>
    internal static class VillageSurfaceAppearance
    {
        internal const string ResourceFolder = "Village/Textures/Surfaces/";
        internal const string ManifestResourcePath = ResourceFolder + "VillageSurfaceTextures";
        internal const string ShaderPath = "Shaders/VillageSurfaceLit";
        internal const string ShaderName = "Bar Promenade/Village Surface";
        internal const string UniqueAlbedoPath = ResourceFolder + "VillageUniqueAlbedo";
        internal const string UniqueResponsePath = ResourceFolder + "VillageUniqueResponse";
        internal const long TextureBytesBudget = 96L * 1024L * 1024L;
        internal static readonly string[] Families =
        {
            "Timber", "Joinery", "Stone", "Roof", "Plaster", "Concrete", "RustedIron", "PaintedMetal",
            "Enamel", "Linoleum", "WallPaint", "Cloth", "Asphalt", "ForestFloor", "Snow", "Needles", "Bark",
            "AbandonedWood", "AbandonedPlaster", "AbandonedRoof", "WreckRust", "WreckPaint",
            "LodgeFloorWood", "LodgeHatchWood", "LodgePaintedWood", "LodgeOakWood", "LodgePaleWood", "LodgeDarkWood", "LodgeRoughWood"
        };
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        private static VillageSurfaceManifest manifest;
        private static Material sharedMaterial, foliageMaterial;
        private static Texture2D uniqueAlbedo, uniqueResponse;

        internal static VillageSurfaceManifest Manifest
        {
            get
            {
                if (manifest != null) return manifest;
                TextAsset text = Resources.Load<TextAsset>(ManifestResourcePath);
                if (text == null) throw new InvalidOperationException("Missing village surface manifest.");
                return manifest = ParseManifestOrThrow(text.text);
            }
        }

        internal static Material SharedMaterial
        {
            get
            {
                if (sharedMaterial != null) return sharedMaterial;
                Shader shader = Resources.Load<Shader>(ShaderPath);
                if (shader == null || !shader.isSupported) throw new InvalidOperationException("Missing/unsupported village surface shader.");
                sharedMaterial = new Material(RuntimePrimitiveFactory.DefaultMaterial)
                {
                    name = "Alpine Village Surfaces (Shared)", shader = shader,
                    hideFlags = HideFlags.HideAndDontSave, enableInstancing = true
                };
                sharedMaterial.SetFloat("_SceneResponseStrength", 1f);
                sharedMaterial.SetFloat("_SceneNormalStrength", .65f);
                return sharedMaterial;
            }
        }

        internal static Material FoliageMaterial
        {
            get
            {
                if (foliageMaterial != null) return foliageMaterial;
                Shader shader = Resources.Load<Shader>("Shaders/VillageSurfaceFoliage");
                if (shader == null || !shader.isSupported) throw new InvalidOperationException("Missing village wind surface shader.");
                foliageMaterial = new Material(MountainRoadSurfaceAppearance.FoliageMaterial)
                {
                    name = "Alpine Village Wind Surfaces (Shared)", shader = shader,
                    hideFlags = HideFlags.HideAndDontSave, enableInstancing = true
                };
                foliageMaterial.SetFloat("_SceneResponseStrength", 1f);
                foliageMaterial.SetFloat("_SceneNormalStrength", .65f);
                return foliageMaterial;
            }
        }

        internal static VillageSurfaceManifest ParseManifestOrThrow(string json)
        {
            VillageSurfaceManifest data = JsonUtility.FromJson<VillageSurfaceManifest>(json);
            if (data == null || data.design_id != "village_material_surfaces_v1" || data.generator_version != "1.0.0" ||
                data.gpu_budget_bytes != TextureBytesBudget || data.estimated_gpu_bytes <= 0 || data.estimated_gpu_bytes > TextureBytesBudget ||
                data.sheets == null || data.sheets.Length != Families.Length)
                throw new InvalidOperationException("Invalid village surface manifest/budget.");
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (VillageSurfaceSheet sheet in data.sheets)
            {
                if (sheet == null || Array.IndexOf(Families, sheet.family) < 0 || !found.Add(sheet.family) ||
                    sheet.albedo != ResourceFolder + sheet.family + "Albedo" || sheet.response != ResourceFolder + sheet.family + "Response" ||
                    !Hash(sheet.albedo_sha256) || !Hash(sheet.response_sha256) ||
                    sheet.size < 512 || sheet.size > 2048 || sheet.response_size != sheet.size / 2 ||
                    !FinitePositive(sheet.meters_per_tile) || !FinitePositive(sheet.mean_linear_luminance) || sheet.mean_linear_luminance > 1f ||
                    (!string.IsNullOrEmpty(sheet.ground_response) &&
                     (sheet.ground_response != ResourceFolder + sheet.family + "GroundResponse" || !Hash(sheet.ground_response_sha256))))
                    throw new InvalidOperationException("Village surface family contract drifted: " + sheet?.family);
            }
            return data;
        }

        private static bool Hash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char character in value) if (!Uri.IsHexDigit(character)) return false;
            return true;
        }
        private static bool FinitePositive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        internal static VillageSurfaceSheet GetSheet(string family)
        {
            foreach (VillageSurfaceSheet sheet in Manifest.sheets) if (sheet.family == family) return sheet;
            throw new InvalidOperationException("Unknown village surface family: " + family);
        }

        private static Texture2D LoadTexture(string path)
        {
            if (!Textures.TryGetValue(path, out Texture2D texture))
            {
                texture = Resources.Load<Texture2D>(path);
                if (texture == null) throw new InvalidOperationException("Missing village surface texture: " + path);
                Textures.Add(path, texture);
            }
            return texture;
        }

        internal static string GetFamily(MountainRoadSurfaceKind kind)
        {
            switch (kind)
            {
                case MountainRoadSurfaceKind.LayeredStone: return "Stone";
                case MountainRoadSurfaceKind.WindSnow: return "Snow";
                case MountainRoadSurfaceKind.ConiferNeedles: return "Needles";
                case MountainRoadSurfaceKind.BarkAndDeadwood: return "Bark";
                case MountainRoadSurfaceKind.PaleEnamel: return "Enamel";
                case MountainRoadSurfaceKind.Masonry: return "Plaster";
                case MountainRoadSurfaceKind.InteriorPaint: return "WallPaint";
                default: return kind.ToString();
            }
        }

        internal static HomeSurfaceRecipe GetRecipe(MountainRoadSurfaceKind kind)
        {
            HomeSurfaceRecipe previous = MountainRoadSurfaceAppearance.GetRecipe(kind);
            VillageSurfaceSheet sheet = GetSheet(GetFamily(kind));
            return new HomeSurfaceRecipe(sheet.albedo, previous.MetersPerTile, previous.Smoothness, previous.Metallic, previous.AlbedoCompensation);
        }
        internal static Texture2D GetTexture(MountainRoadSurfaceKind kind) => LoadTexture(GetSheet(GetFamily(kind)).albedo);
        internal static Texture2D GetGroundResponse(string family)
        {
            string path = GetSheet(family).ground_response;
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Missing village ground companion: " + family);
            return LoadTexture(path);
        }
        internal static Color CreateDisplayTint(Color tint, MountainRoadSurfaceKind kind) => CreateDisplayTint(tint, GetSheet(GetFamily(kind)));
        internal static Color CreateDisplayTint(Color tint, VillageSurfaceSheet sheet)
        {
            if (sheet.full_color) return Color.white;
            float mean = sheet.mean_linear_luminance;
            return new Color(Compensate(tint.r, mean), Compensate(tint.g, mean), Compensate(tint.b, mean), tint.a);
        }
        private static float Compensate(float value, float mean) => Mathf.LinearToGammaSpace(Mathf.Clamp01(Mathf.GammaToLinearSpace(value) / mean));

        internal static void Apply(Renderer renderer, MountainRoadSurfaceKind kind, Color tint, int materialIndex = -1)
        {
            if (renderer != null) Apply(renderer, kind, SurfaceAppearanceCore.ResolveBoxProjection(renderer), tint, materialIndex);
        }
        internal static void Apply(Renderer renderer, MountainRoadSurfaceKind kind, SurfaceProjection projection, Color tint, int materialIndex = -1)
        {
            if (renderer == null) return;
            HomeSurfaceRecipe recipe = GetRecipe(kind);
            Vector4 uv = SurfaceAppearanceCore.CreateBaseMapTransform(renderer, recipe.MetersPerTile, .35f, projection, 29000 + (int)kind);
            ApplyFamily(renderer, GetFamily(kind), tint, uv, recipe.Metallic, materialIndex);
            ApplyGround(renderer, kind, materialIndex);
        }
        internal static void ApplyCombined(Renderer renderer, MountainRoadSurfaceKind kind, Color tint) => ApplyCombined(renderer, kind, tint, -1);
        internal static void ApplyCombined(Renderer renderer, MountainRoadSurfaceKind kind, Color tint, int materialIndex)
        {
            ApplyFamily(renderer, GetFamily(kind), tint, new Vector4(1f, 1f, 0f, 0f), GetRecipe(kind).Metallic, materialIndex);
            ApplyGround(renderer, kind, materialIndex);
        }
        internal static void ApplyCombined(Renderer renderer, MountainRoadSurfaceKind kind, Color tint, Material shared)
        {
            if (renderer == null) return;
            // Wind crowns keep their existing shared vertex/shadow passes.
            GroundSurfaceAppearance.AssignSharedMaterial(renderer, shared, -1);
            Read(renderer, -1);
            Properties.SetTexture("_BaseMap", GetTexture(kind));
            Properties.SetTexture("_SceneMaterialResponse", LoadTexture(GetSheet(GetFamily(kind)).response));
            Properties.SetFloat("_SceneResponseStrength", 1f); Properties.SetFloat("_SceneNormalStrength", .65f);
            Properties.SetColor("_BaseColor", CreateDisplayTint(tint, kind));
            Properties.SetColor("_Color", CreateDisplayTint(tint, kind));
            Properties.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
            Properties.SetFloat("_Smoothness", GetRecipe(kind).Smoothness);
            Properties.SetFloat("_Metallic", GetRecipe(kind).Metallic);
            Write(renderer, -1);
        }

        internal static void ApplyFamily(Renderer renderer, string family, Color tint, Vector4 uv, float metallic = 0f, int materialIndex = -1)
        {
            if (renderer == null) return;
            VillageSurfaceSheet sheet = GetSheet(family);
            GroundSurfaceAppearance.AssignSharedMaterial(renderer, SharedMaterial, materialIndex);
            Read(renderer, materialIndex);
            Properties.SetTexture("_BaseMap", LoadTexture(sheet.albedo));
            Properties.SetTexture("_SceneMaterialResponse", LoadTexture(sheet.response));
            Properties.SetVector("_BaseMap_ST", uv);
            Color display = CreateDisplayTint(tint, sheet);
            Properties.SetColor("_BaseColor", display); Properties.SetColor("_Color", display);
            Properties.SetFloat("_Metallic", metallic);
            Properties.SetFloat("_SceneResponseStrength", 1f); Properties.SetFloat("_SceneNormalStrength", .65f);
            Write(renderer, materialIndex);
        }

        internal static void ApplyUnique(Renderer renderer, string originalSurface)
        {
            if (renderer == null) return;
            if (uniqueAlbedo == null) uniqueAlbedo = LoadTexture(UniqueAlbedoPath);
            if (uniqueResponse == null) uniqueResponse = LoadTexture(UniqueResponsePath);
            renderer.sharedMaterial = SharedMaterial;
            Read(renderer, -1);
            Properties.SetTexture("_BaseMap", uniqueAlbedo); Properties.SetTexture("_SceneMaterialResponse", uniqueResponse);
            Properties.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
            Properties.SetColor("_BaseColor", Color.white); Properties.SetColor("_Color", Color.white);
            float metallic = originalSurface == "LighterMetal" ? .72f : originalSurface == "RustedIron" ? .2f :
                originalSurface == "PaintedMetal" || originalSurface == "PaleEnamel" ? .15f :
                originalSurface == "WreckRust" || originalSurface == "WreckPaint" ? .02f : 0f;
            Properties.SetFloat("_Metallic", metallic);
            Properties.SetFloat("_SceneResponseStrength", 1f); Properties.SetFloat("_SceneNormalStrength", .65f);
            Write(renderer, -1);
        }

        internal static void ApplyAuthoredGround(Renderer renderer, GroundSurfaceKind kind, string family, int materialIndex = -1)
        {
            if (renderer == null) return;
            Texture2D albedo = LoadTexture(GetSheet(family).albedo);
            Read(renderer, materialIndex);
            Properties.SetTexture("_BaseMap", albedo);
            Write(renderer, materialIndex);
            GroundSurfaceAppearance.Apply(renderer, kind, albedo, materialIndex, GetGroundResponse(family),
                configureSnowSubstrate: false);
        }

        private static void ApplyGround(Renderer renderer, MountainRoadSurfaceKind kind, int index)
        {
            GroundSurfaceKind ground;
            switch (kind)
            {
                case MountainRoadSurfaceKind.Asphalt: ground = GroundSurfaceKind.Asphalt; break;
                case MountainRoadSurfaceKind.ForestFloor: ground = GroundSurfaceKind.Soil; break;
                case MountainRoadSurfaceKind.WindSnow: ground = GroundSurfaceKind.Snow; break;
                case MountainRoadSurfaceKind.LayeredStone: ground = GroundSurfaceKind.Rock; break;
                case MountainRoadSurfaceKind.Concrete: ground = GroundSurfaceKind.Concrete; break;
                case MountainRoadSurfaceKind.Masonry: ground = GroundSurfaceKind.Paving; break;
                case MountainRoadSurfaceKind.Timber: ground = GroundSurfaceKind.Timber; break;
                default: return;
            }
            if (renderer == null) return;
            ApplyAuthoredGround(renderer, ground, GetFamily(kind), index);
            if (ground == GroundSurfaceKind.Snow)
            {
                Read(renderer, index);
                Properties.SetTexture("_GroundSubstrateMap", GetTexture(MountainRoadSurfaceKind.ForestFloor));
                Properties.SetTexture("_GroundSubstrateResponse", GetGroundResponse("ForestFloor"));
                Properties.SetTexture("_GroundAsphaltMap", GetTexture(MountainRoadSurfaceKind.Asphalt));
                Properties.SetTexture("_GroundAsphaltResponse", GetGroundResponse("Asphalt"));
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
            if (sharedMaterial != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(sharedMaterial);
                else UnityEngine.Object.DestroyImmediate(sharedMaterial);
            }
            if (foliageMaterial != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(foliageMaterial);
                else UnityEngine.Object.DestroyImmediate(foliageMaterial);
            }
            sharedMaterial = foliageMaterial = null; manifest = null; uniqueAlbedo = uniqueResponse = null; Textures.Clear();
        }
    }

    [Serializable]
    internal sealed class VillageSurfaceManifest
    {
        public string design_id, generator_version;
        public long gpu_budget_bytes, estimated_gpu_bytes;
        public VillageSurfaceSheet[] sheets;
        public VillageSurfaceAdditionalMap[] additional_maps;
    }
    [Serializable]
    internal sealed class VillageSurfaceSheet
    {
        public string family, albedo, response, ground_response, albedo_sha256, response_sha256, ground_response_sha256;
        public int size, response_size;
        public float meters_per_tile, mean_linear_luminance;
        public bool full_color;
    }
    [Serializable]
    internal sealed class VillageSurfaceAdditionalMap
    {
        public string resource, sha256;
        public int size;
        public bool mipmaps, sRGB;
    }
}
