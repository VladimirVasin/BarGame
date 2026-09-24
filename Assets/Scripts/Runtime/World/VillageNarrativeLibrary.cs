using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class VillageNarrativeDefinition
    {
        public int id;
        public string kind, label, origin;
        public float[] bounds_min, bounds_max;
        public VillageLifePropAnchor[] anchors;
        public VillageLifePropPart[] parts;
    }

    [Serializable]
    public sealed class VillageNarrativeManifest
    {
        public string generator_version, design_id, build_signature, scale_mode, uv_mode, forward;
        public bool colliders, lights, cameras;
        public int animation_count, prop_count, mesh_count, triangle_count;
        public VillageNarrativeDefinition[] props;
    }

    /// <summary>Thirty authored environmental compositions, including two wall
    /// sheets. All coordinates are actual metres, ground origin and front +Z.</summary>
    public static class VillageNarrativeLibrary
    {
        public const string ResourcePath = "VillageNarrative/VillageNarrative3D";
        public const string GeneratorVersion = "1.0.0";
        public const string DesignId = "village_narrative_30_v1";
        public const int ExpectedPropCount = 30;
        private static VillageNarrativeManifest manifest;
        private static Dictionary<int, VillageNarrativeDefinition> definitions;
        private static Dictionary<string, MeshFilter> meshes;

        public static VillageNarrativeManifest Manifest { get { LoadOrThrow(); return manifest; } }

        public static void LoadOrThrow()
        {
            if (manifest != null && meshes != null) return;
            var source = Resources.Load<GameObject>(ResourcePath);
            var text = Resources.Load<TextAsset>(ResourcePath);
            if (source == null || text == null)
                throw new InvalidOperationException("Missing authored village narrative FBX/manifest.");
            var loaded = JsonUtility.FromJson<VillageNarrativeManifest>(text.text);
            if (loaded == null || loaded.design_id != DesignId || loaded.generator_version != GeneratorVersion ||
                loaded.scale_mode != "fixed_metres" || loaded.uv_mode != "projected_metres" ||
                loaded.prop_count != ExpectedPropCount || loaded.props == null || loaded.props.Length != ExpectedPropCount ||
                loaded.colliders || loaded.lights || loaded.cameras || loaded.animation_count != 0)
                throw new InvalidOperationException("Village narrative assets violate the passive metre contract.");
            var found = new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == null || !found.TryAdd(filter.sharedMesh.name, filter))
                    throw new InvalidOperationException("Duplicate or empty village narrative mesh.");
            if (found.Count != loaded.mesh_count)
                throw new InvalidOperationException("Village narrative FBX and manifest differ.");
            var indexed = new Dictionary<int, VillageNarrativeDefinition>();
            foreach (var definition in loaded.props)
            {
                if (definition.id < 1 || definition.id > 32 || definition.id == 26 || definition.id == 27 ||
                    definition.kind != "N" + definition.id.ToString("00") || !indexed.TryAdd(definition.id, definition))
                    throw new InvalidOperationException("Unexpected village narrative model ID.");
                foreach (var part in definition.parts)
                    if (!found.ContainsKey(part.mesh))
                        throw new InvalidOperationException("Missing narrative model part: " + part.mesh);
            }
            meshes = found; definitions = indexed; manifest = loaded;
        }

        public static VillageNarrativeDefinition GetDefinition(int id)
        {
            LoadOrThrow();
            if (definitions.TryGetValue(id, out var result)) return result;
            throw new ArgumentOutOfRangeException(nameof(id), "The truck and chair pile retain their existing models.");
        }

        public static Bounds GetBounds(int id)
        {
            var definition = GetDefinition(id);
            var result = new Bounds();
            result.SetMinMax(VillageLifePropLibrary.Vector(definition.bounds_min), VillageLifePropLibrary.Vector(definition.bounds_max));
            return result;
        }

        public static Vector3 GetAnchor(int id, string name)
        {
            foreach (var anchor in GetDefinition(id).anchors)
                if (anchor.name == name) return VillageLifePropLibrary.Vector(anchor.position);
            throw new InvalidOperationException($"Narrative model {id:00} has no anchor {name}.");
        }

        public static GameObject Create(int id, Transform parent, string name = null, bool collision = true)
        {
            var definition = GetDefinition(id);
            var root = new GameObject(name ?? "Village narrative " + id.ToString("00"));
            root.transform.SetParent(parent, false);
            foreach (var part in definition.parts)
            {
                var template = meshes[part.mesh];
                var host = new GameObject(part.mesh);
                host.transform.SetParent(root.transform, false);
                // Imported FBX centimetres retain their metre multiplier on
                // the authoring root. Copy the complete transform, not one.
                host.transform.localPosition = template.transform.position;
                host.transform.localRotation = template.transform.rotation;
                host.transform.localScale = template.transform.lossyScale;
                host.AddComponent<MeshFilter>().sharedMesh = template.sharedMesh;
                var renderer = host.AddComponent<MeshRenderer>();
                ApplySurface(renderer, part);
                // Exact static solids preserve the empty spaces of frames,
                // wheels and rails. No invisible bounding box blocks paths.
                if (collision) host.AddComponent<MeshCollider>().sharedMesh = template.sharedMesh;
            }
            foreach (var anchor in definition.anchors)
            {
                var host = new GameObject("ANCHOR_" + anchor.name);
                host.transform.SetParent(root.transform, false);
                host.transform.localPosition = VillageLifePropLibrary.Vector(anchor.position);
            }
            return root;
        }

        private static void ApplySurface(MeshRenderer renderer, VillageLifePropPart part)
        {
            var tint = new Color(part.tint[0], part.tint[1], part.tint[2], part.tint[3]);
            if (part.surface == "Cloth" || part.surface == "Paper")
            {
                var kind = part.surface == "Paper" ? CityPointOfInterestSurfaceKind.Paper : CityPointOfInterestSurfaceKind.Cloth;
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                var properties = new MaterialPropertyBlock();
                properties.SetTexture("_BaseMap", CityPointOfInterestSurfaceAppearance.GetTexture(kind));
                var display = CityPointOfInterestSurfaceAppearance.CreateDisplayTint(tint, kind);
                properties.SetColor("_BaseColor", display); properties.SetColor("_Color", display);
                float pitch = 1f / CityPointOfInterestSurfaceAppearance.GetRecipe(kind).MetersPerTile;
                properties.SetVector("_BaseMap_ST", new Vector4(pitch, pitch, 0, 0));
                properties.SetFloat("_Smoothness", 0); properties.SetFloat("_Metallic", 0);
                renderer.SetPropertyBlock(properties);
                return;
            }
            if (part.surface == "Rubber" || part.surface == "DullGlass")
            {
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                var properties = new MaterialPropertyBlock();
                properties.SetColor("_BaseColor", tint); properties.SetColor("_Color", tint);
                properties.SetFloat("_Smoothness", .02f); properties.SetFloat("_Metallic", 0);
                renderer.SetPropertyBlock(properties);
                return;
            }
            var surfaceName = part.surface == "BareStone" ? "LayeredStone" : part.surface;
            if (!Enum.TryParse(surfaceName, out MountainRoadSurfaceKind surface))
                throw new InvalidOperationException("Unknown village narrative surface: " + part.surface);
            MountainRoadSurfaceAppearance.ApplyCombined(renderer, surface, tint);
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            float uvScale = 1f / MountainRoadSurfaceAppearance.GetRecipe(surface).MetersPerTile;
            block.SetVector("_BaseMap_ST", new Vector4(uvScale, uvScale, 0, 0));
            renderer.SetPropertyBlock(block);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { manifest = null; definitions = null; meshes = null; }
    }
}
