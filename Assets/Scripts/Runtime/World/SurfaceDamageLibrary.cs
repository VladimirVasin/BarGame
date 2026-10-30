using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Finite authored charts, sampled once per whole surface over the original microtexture.</summary>
    internal static class SurfaceDamageLibrary
    {
        [Serializable] internal sealed class Entry
        {
            public string key, chart_id, domain, family, projection, space;
            public int page;
            public float[] rect, bounds_min, bounds_max, room_point_scale, room_point_offset;
            public bool use_renderer_bounds;
        }
        [Serializable] private sealed class Family
        {
            public string domain, family;
            public float[] soil_multiplier, expose_color, accent_color;
            public float smoothness_scale;
            public string micro_resource_override;
            [NonSerialized] public Texture2D Micro;
        }
        [Serializable] private sealed class Page { public string resource; }
        [Serializable] private sealed class Manifest { public Entry[] entries; public Family[] families; public Page[] pages; }

        private static Manifest manifest;
        private static Dictionary<string, Entry> entries;
        private static Dictionary<string, Family> families;
        private static Texture2D[] pages;
        private static Material lit;
        private static readonly int AtlasId = Shader.PropertyToID("_SurfaceDamageAtlas");
        private static readonly int RectId = Shader.PropertyToID("_SurfaceDamageRect");
        private static readonly int MinId = Shader.PropertyToID("_SurfaceDamageBoundsMin");
        private static readonly int SizeId = Shader.PropertyToID("_SurfaceDamageInvSize");
        private static readonly int MatrixId = Shader.PropertyToID("_SurfaceDamageObjectToDomain");
        private static readonly int ModeId = Shader.PropertyToID("_SurfaceDamageMode");
        private static readonly int SoilId = Shader.PropertyToID("_SurfaceDamageSoil");
        private static readonly int ExposeId = Shader.PropertyToID("_SurfaceDamageExpose");
        private static readonly int AccentId = Shader.PropertyToID("_SurfaceDamageAccent");
        private static readonly int ResponseId = Shader.PropertyToID("_SurfaceDamageResponse");

        private static void Ensure()
        {
            if (manifest != null) return;
            TextAsset source = Resources.Load<TextAsset>("SurfaceDamage/SurfaceDamageManifest");
            if (source == null) throw new InvalidOperationException("Missing unique surface damage manifest.");
            manifest = JsonUtility.FromJson<Manifest>(source.text);
            if (manifest?.entries == null || manifest.families == null || manifest.pages == null || manifest.pages.Length != 2)
                throw new InvalidOperationException("Invalid bounded surface damage atlas.");
            entries = new Dictionary<string, Entry>(manifest.entries.Length, StringComparer.Ordinal);
            families = new Dictionary<string, Family>(manifest.families.Length, StringComparer.Ordinal);
            foreach (Entry entry in manifest.entries) entries.Add(entry.key, entry);
            foreach (Family family in manifest.families) families.Add(family.domain + ":" + family.family, family);
            pages = new Texture2D[manifest.pages.Length];
        }

        internal static Entry Resolve(string domain, string authoredName, string semanticName, string family)
        {
            Ensure();
            Entry value;
            bool profile = authoredName != null && authoredName.StartsWith("Profile.", StringComparison.Ordinal);
            if (!profile && authoredName != null && entries.TryGetValue(domain + ":" + authoredName, out value)) return value;
            if (semanticName != null && entries.TryGetValue(domain + ":" + semanticName, out value)) return value;
            if (authoredName != null && entries.TryGetValue(domain + ":" + authoredName, out value)) return value;
            if (entries.TryGetValue(domain + ":family:" + family, out value)) return value;
            throw new InvalidOperationException("Missing surface damage chart for " + domain + ":" + semanticName);
        }

        private static Material Lit
        {
            get
            {
                if (lit == null)
                {
                    Shader shader = Shader.Find("Bar Promenade/Surface Damage Lit");
                    if (shader == null) throw new InvalidOperationException("Missing surface damage Lit shader.");
                    lit = new Material(shader) { name = "Shared Surface Damage Lit", hideFlags = HideFlags.HideAndDontSave };
                    lit.CopyPropertiesFromMaterial(RuntimePrimitiveFactory.DefaultMaterial);
                    // Charts and their domain matrices are per draw, not
                    // instance-buffer properties. Keep that lighting path explicit.
                    lit.enableInstancing = false;
                }
                return lit;
            }
        }

        internal static Material RestoreOccluderMaterial(Material original, MaterialPropertyBlock properties) =>
            properties.GetVector(ModeId).x > 0f ? Lit : original;

        internal static void Apply(Renderer renderer, MaterialPropertyBlock properties, Entry entry,
            Transform room, float strength, Material originalMaterial = null)
        {
            bool dither = renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null &&
                renderer.sharedMaterial.shader.name == "Bar Promenade/Home Occluder Dither";
            if (strength <= 0f)
            {
                properties.SetVector(ModeId, Vector4.zero);
                if (!dither && originalMaterial != null) renderer.sharedMaterial = originalMaterial;
                return;
            }
            if (!dither) renderer.sharedMaterial = Lit;
            Ensure();
            if (pages[entry.page] == null)
                pages[entry.page] = Resources.Load<Texture2D>(manifest.pages[entry.page].resource);
            if (pages[entry.page] == null) throw new InvalidOperationException("Missing damage atlas page.");
            Family family = families[entry.domain + ":" + entry.family];
            if (!string.IsNullOrEmpty(family.micro_resource_override))
            {
                if (family.Micro == null) family.Micro = Resources.Load<Texture2D>(family.micro_resource_override);
                if (family.Micro == null) throw new InvalidOperationException("Missing neutral surface microtexture.");
                properties.SetTexture("_BaseMap", family.Micro);
            }
            Matrix4x4 toDomain = Matrix4x4.identity;
            Bounds bounds;
            if (entry.space == "room")
            {
                if (room == null) throw new InvalidOperationException("A continuous room chart needs its source room.");
                toDomain = room.worldToLocalMatrix * renderer.localToWorldMatrix;
                if (entry.room_point_scale != null || entry.room_point_offset != null)
                    toDomain = Matrix4x4.TRS(Vector(entry.room_point_offset), Quaternion.identity,
                        entry.room_point_scale == null ? Vector3.one : Vector(entry.room_point_scale)) * toDomain;
                Vector3 min = Vector(entry.bounds_min), max = Vector(entry.bounds_max);
                bounds = new Bounds((min + max) * .5f, max - min);
            }
            else
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) throw new InvalidOperationException("A damage chart requires its original mesh bounds.");
                bounds = mesh.bounds;
            }
            Vector3 size = bounds.size;
            properties.SetTexture(AtlasId, pages[entry.page]);
            properties.SetVector(RectId, new Vector4(entry.rect[0], entry.rect[1], entry.rect[2], entry.rect[3]));
            properties.SetVector(MinId, bounds.min);
            properties.SetVector(SizeId, new Vector4(1f / Mathf.Max(size.x, .0001f),
                1f / Mathf.Max(size.y, .0001f), 1f / Mathf.Max(size.z, .0001f), 0f));
            properties.SetMatrix(MatrixId, toDomain);
            float projection = entry.projection == "XY" ? 0f : entry.projection == "ZY" ? 1f : entry.projection == "XZ" ? 2f : 3f;
            properties.SetVector(ModeId, new Vector4(strength, projection, 0f, 0f));
            properties.SetColor(SoilId, Color(family.soil_multiplier));
            properties.SetColor(ExposeId, Color(family.expose_color).linear);
            properties.SetColor(AccentId, Color(family.accent_color).linear);
            properties.SetVector(ResponseId, new Vector4(family.smoothness_scale, 0f, 0f, 0f));
        }

        private static Vector3 Vector(float[] v) => v == null ? Vector3.zero : new Vector3(v[0], v[1], v[2]);
        private static Color Color(float[] v) => new Color(v[0], v[1], v[2], 1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (lit != null) UnityEngine.Object.Destroy(lit);
            lit = null; manifest = null; entries = null; families = null; pages = null;
        }
    }
}
