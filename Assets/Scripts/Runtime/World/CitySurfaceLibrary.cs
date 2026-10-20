using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>City-owned maps bound after semantic construction, including consolidated indexed ground slots.</summary>
    internal static class CitySurfaceLibrary
    {
        internal const string ManifestResourcePath = "City/Surfaces/CitySurfaceTextures";
        internal const string ShaderName = "Bar Promenade/City Surface";
        internal const string FoliageShaderName = "Bar Promenade/City Surface Foliage";
        internal const long MaximumTextureBytesBudget = 224L * 1024L * 1024L;
        private const int MaximumSheets = 512, MaximumSharedMaterials = 512, MaximumSceneBindings = 131072;
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int ResponseId = Shader.PropertyToID("_SceneMaterialResponse");
        private static readonly int GroundResponseId = Shader.PropertyToID("_GroundResponse");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int TransformId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int ResponseStrengthId = Shader.PropertyToID("_SceneResponseStrength");
        private static readonly int NormalStrengthId = Shader.PropertyToID("_SceneNormalStrength");
        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<CitySurfaceSheet>> sourceNames = new Dictionary<string, List<CitySurfaceSheet>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, CitySurfaceSheet> uniqueBarNames = new Dictionary<string, CitySurfaceSheet>(StringComparer.Ordinal);
        private static readonly Dictionary<Texture, CitySurfaceSheet> resolvedSources = new Dictionary<Texture, CitySurfaceSheet>();
        private static readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
        private static readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private static readonly MaterialPropertyBlock indexed = new MaterialPropertyBlock();
        private static CitySurfaceManifest manifest;

        internal static int SharedMaterialCount => materials.Count;
        internal static CitySurfaceManifest Manifest
        {
            get
            {
                if (manifest != null) return manifest;
                TextAsset asset = Resources.Load<TextAsset>(ManifestResourcePath);
                if (asset == null) throw new InvalidOperationException("Missing City surface manifest.");
                manifest = ParseManifestOrThrow(asset.text);
                foreach (CitySurfaceSheet sheet in manifest.sheets)
                {
                    if (sheet.unique_uv)
                    {
                        foreach (string name in sheet.renderer_names) uniqueBarNames.Add(name, sheet);
                        continue;
                    }
                    string nameKey = !string.IsNullOrEmpty(sheet.source_name) ? sheet.source_name : ResourceName(sheet.source);
                    if (!sourceNames.TryGetValue(nameKey, out List<CitySurfaceSheet> candidates))
                        sourceNames.Add(nameKey, candidates = new List<CitySurfaceSheet>(1));
                    candidates.Add(sheet);
                }
                return manifest;
            }
        }

        internal static CitySurfaceManifest ParseManifestOrThrow(string json)
        {
            CitySurfaceManifest value = JsonUtility.FromJson<CitySurfaceManifest>(json);
            if (value == null || value.design_id != "city_material_surfaces_v1" || value.generator_version != "1.0.0" ||
                value.gpu_budget_bytes <= 0 || value.gpu_budget_bytes > MaximumTextureBytesBudget ||
                value.estimated_gpu_bytes <= 0 || value.estimated_gpu_bytes > value.gpu_budget_bytes ||
                value.sheets == null || value.sheets.Length == 0 || value.sheets.Length > MaximumSheets)
                throw new InvalidOperationException("Invalid City surface manifest or texture budget.");
            var families = new HashSet<string>(StringComparer.Ordinal);
            var sources = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (CitySurfaceSheet sheet in value.sheets)
            {
                if (sheet == null || string.IsNullOrEmpty(sheet.family) || !families.Add(sheet.family) ||
                    !IsCityResource(sheet.albedo) || !IsCityResource(sheet.response) ||
                    sheet.size <= 0 || sheet.response_size <= 0 || sheet.mean_linear_luminance <= 0f)
                    throw new InvalidOperationException("Invalid City surface sheet.");
                if (!string.IsNullOrEmpty(sheet.ground_response) && !IsCityResource(sheet.ground_response))
                    throw new InvalidOperationException("City ground response must remain scene-owned.");
                if (sheet.unique_uv)
                {
                    if (sheet.renderer_names == null || sheet.renderer_names.Length == 0)
                        throw new InvalidOperationException("A unique City sheet needs explicit facade source names.");
                    foreach (string name in sheet.renderer_names)
                        if (string.IsNullOrEmpty(name) || !names.Add(name)) throw new InvalidOperationException("Duplicate unique City facade source name.");
                }
                else if ((string.IsNullOrEmpty(sheet.source) && string.IsNullOrEmpty(sheet.source_name)) ||
                    !sources.Add(!string.IsNullOrEmpty(sheet.source) ? sheet.source : "name:" + sheet.source_name))
                    throw new InvalidOperationException("Each City surface source must have one binding.");
            }
            return value;
        }

        private static bool IsCityResource(string path) => !string.IsNullOrEmpty(path) &&
            path.StartsWith("City/Surfaces/", StringComparison.Ordinal) && path.IndexOf("..", StringComparison.Ordinal) < 0;
        private static string ResourceName(string path) => path.Substring(path.LastIndexOf('/') + 1);

        internal static int ApplyHierarchy(Transform root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            _ = Manifest;
            int bound = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is not MeshRenderer || renderer.GetComponentInParent<Animator>(true) != null ||
                    renderer.GetComponentInParent<VillageResidentPresentation>(true) != null ||
                    renderer.GetComponentInParent<PlayerMotor>(true) != null) continue;
                bound += ApplyRenderer(renderer);
                if (bound > MaximumSceneBindings) throw new InvalidOperationException("City surface binding budget exceeded.");
            }
            return bound;
        }

        internal static int ApplyRenderer(Renderer renderer)
        {
            if (renderer == null) return 0;
            _ = Manifest;
            int bound = 0;
            Material[] slots = renderer.sharedMaterials;
            bool changedMaterials = false;
            for (int slot = 0; slot < slots.Length; slot++)
            {
                Material original = slots[slot];
                if (!CanBind(original, out bool ground, out bool foliage)) continue;
                indexed.Clear(); renderer.GetPropertyBlock(indexed, slot);
                bool hasIndex = !indexed.isEmpty;
                properties.Clear(); renderer.GetPropertyBlock(properties);
                Texture texture = hasIndex && indexed.HasProperty(BaseMapId) ? indexed.GetTexture(BaseMapId) :
                    properties.HasProperty(BaseMapId) ? properties.GetTexture(BaseMapId) : original.GetTexture(BaseMapId);
                if (!TryResolve(texture, out CitySurfaceSheet sheet) || !IsSourceScope(renderer, original, sheet)) continue;
                if (ground && string.IsNullOrEmpty(sheet.ground_response))
                    throw new InvalidOperationException("Missing packed ground response for City source: " + sheet.source);
                int materialIndex = slots.Length == 1 && !hasIndex ? -1 : slot;
                if (hasIndex) renderer.GetPropertyBlock(properties, slot);
                // An indexed block has precedence over the renderer block. A new indexed block starts with
                // the complete existing renderer block, retaining tint, wetness, wind and road-mask values.
                BindMaps(properties, sheet, ground);
                GroundSurfaceAppearance.WriteProperties(renderer, properties, materialIndex);
                if (!ground) { slots[slot] = GetShared(original, foliage); changedMaterials = true; }
                bound++;
            }
            if (changedMaterials) renderer.sharedMaterials = slots;
            return bound;
        }

        // The builder calls this while it still owns BarAssetRegistry metadata, before flattening/destroying it.
        // Generic City or interior source names never opt into the unique facade atlas.
        internal static void ApplyBarFacade(Renderer renderer, string sourceName)
        {
            _ = Manifest;
            if (renderer == null || !uniqueBarNames.TryGetValue(sourceName, out CitySurfaceSheet sheet) ||
                !CanBind(renderer.sharedMaterial, out bool ground, out bool foliage) || ground) return;
            properties.Clear(); renderer.GetPropertyBlock(properties);
            BindMaps(properties, sheet, false);
            renderer.sharedMaterial = GetShared(renderer.sharedMaterial, foliage);
            renderer.SetPropertyBlock(properties);
        }

        private static void BindMaps(MaterialPropertyBlock target, CitySurfaceSheet sheet, bool ground)
        {
            target.SetTexture(BaseMapId, Load(sheet.albedo));
            if (ground) target.SetTexture(GroundResponseId, Load(sheet.ground_response));
            else
            {
                target.SetTexture(ResponseId, Load(sheet.response));
                target.SetFloat(ResponseStrengthId, 1f); target.SetFloat(NormalStrengthId, .65f);
            }
            if (!sheet.unique_uv) return;
            target.SetVector(TransformId, new Vector4(1f, 1f, 0f, 0f));
            if (sheet.full_color) { target.SetColor(BaseColorId, Color.white); target.SetColor(ColorId, Color.white); }
        }

        private static bool CanBind(Material material, out bool ground, out bool foliage)
        {
            ground = foliage = false;
            if (material == null || material.shader == null || !material.HasProperty(BaseMapId) || material.renderQueue >= 2500 ||
                material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") || material.IsKeywordEnabled("_EMISSION")) return false;
            string shader = material.shader.name;
            // Already-bound unique facade maps must not be replaced by a later hierarchy pass.
            if (shader == ShaderName || shader == FoliageShaderName) return false;
            ground = material.HasProperty(GroundResponseId);
            foliage = shader == "Bar Promenade/Lit Foliage";
            return ground || foliage || shader == "Bar Promenade/Lit";
        }

        private static bool TryResolve(Texture source, out CitySurfaceSheet sheet)
        {
            sheet = null;
            if (source == null) return false;
            if (resolvedSources.TryGetValue(source, out sheet)) return sheet != null;
            if (sourceNames.TryGetValue(source.name, out List<CitySurfaceSheet> candidates))
            {
                foreach (CitySurfaceSheet candidate in candidates)
                    // District building sources deliberately repeat names such as Metal; retain their exact owner.
                    if (!string.IsNullOrEmpty(candidate.source) ? Resources.Load<Texture2D>(candidate.source) == source :
                        candidate.source_name == source.name)
                    { sheet = candidate; break; }
            }
            if (resolvedSources.Count >= MaximumSheets * 4) throw new InvalidOperationException("City source texture cache budget exceeded.");
            resolvedSources.Add(source, sheet);
            return sheet != null;
        }

        private static bool IsSourceScope(Renderer renderer, Material material, CitySurfaceSheet sheet)
        {
            if (!string.IsNullOrEmpty(sheet.source)) return true;
            // Church maps are prefab-linked assets outside Resources. Restrict that explicit exception to
            // a registered exterior part and its exact authored material slot, never a same-named interior prop.
            if (string.IsNullOrEmpty(sheet.source_asset) || !sheet.source_asset.StartsWith("Assets/Church/Textures/", StringComparison.Ordinal)) return false;
            ChurchAssetRegistry registry = renderer.GetComponentInParent<ChurchAssetRegistry>(true);
            if (registry == null || registry.Kind != ChurchAssetKind.Exterior) return false;
            foreach (ChurchRendererBinding binding in registry.RendererBindings)
                if (binding.Renderer == renderer && material.name == "Church" + binding.MaterialSlot) return true;
            return false;
        }

        private static Texture2D Load(string path)
        {
            if (textures.TryGetValue(path, out Texture2D result)) return result;
            result = Resources.Load<Texture2D>(path);
            if (result == null) throw new InvalidOperationException("Missing City surface map: " + path);
            textures.Add(path, result); return result;
        }

        private static Material GetShared(Material original, bool foliage)
        {
            if (materials.TryGetValue(original, out Material shared)) return shared;
            if (materials.Count >= MaximumSharedMaterials) throw new InvalidOperationException("City shared surface material budget exceeded.");
            Shader shader = Resources.Load<Shader>(foliage ? "Shaders/CitySurfaceFoliage" : "Shaders/CitySurfaceLit");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Missing supported City surface shader.");
            shared = new Material(original)
            {
                name = original.name + " City Surface Shared", shader = shader,
                hideFlags = HideFlags.HideAndDontSave, enableInstancing = true
            };
            materials.Add(original, shared); return shared;
        }

        internal static bool SupportsOpaque(Renderer renderer) => renderer != null && renderer.sharedMaterial != null &&
            SupportsOpaque(renderer.sharedMaterial);
        internal static bool SupportsOpaque(Material material) => material != null && material.shader != null &&
            (material.shader.name == "Bar Promenade/Lit" || material.shader.name == ShaderName);

        internal static int PropertyIndex(Renderer renderer, int slotCount, int slot, MaterialPropertyBlock scratch)
        {
            if (slotCount > 1) return slot;
            scratch.Clear(); renderer.GetPropertyBlock(scratch, slot);
            return scratch.isEmpty ? -1 : slot;
        }

        internal static void ReadProperties(Renderer renderer, int materialIndex, MaterialPropertyBlock scratch)
        {
            scratch.Clear();
            if (materialIndex >= 0) renderer.GetPropertyBlock(scratch, materialIndex);
            if (materialIndex < 0 || scratch.isEmpty) renderer.GetPropertyBlock(scratch);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            foreach (Material material in materials.Values)
            {
                if (material == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(material); else UnityEngine.Object.DestroyImmediate(material);
            }
            materials.Clear(); textures.Clear(); resolvedSources.Clear(); sourceNames.Clear(); uniqueBarNames.Clear(); manifest = null;
        }
    }

    [Serializable]
    internal sealed class CitySurfaceManifest
    {
        public string design_id, generator_version;
        public long gpu_budget_bytes, estimated_gpu_bytes;
        public CitySurfaceSheet[] sheets;
    }
    [Serializable]
    internal sealed class CitySurfaceSheet
    {
        public string family, source, source_name, source_asset, albedo, response, ground_response;
        public string[] renderer_names;
        public int size, response_size;
        public float mean_linear_luminance, meters_per_tile;
        public bool unique_uv, full_color;
    }
}
