using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded resources and import contract shared by isolated authored humanoid bodies.</summary>
    public sealed class CombatAuthoredBodyBank
    {
        [Serializable] public sealed class BodyManifest
        {
            public string design_id, rig, geometry_owner, bounds_space;
            public bool retained_rest_skeleton;
            public int uv_channel, source_uv_channel;
            public long estimated_gpu_bytes;
            public BodyMesh[] meshes;
            public BodyTexture[] textures;
        }
        [Serializable] public sealed class BodyMesh
        {
            public string name;
            public bool visible;
            public string[] surfaces;
            public float[] bounds_min, bounds_max;
        }
        [Serializable] public sealed class BodyTexture
        {
            public string name, sha256;
            public int size;
            public bool sRGB;
        }

        private BodyManifest manifest;
        private GameObject body;
        private Texture2D atlas, response;
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
        private readonly Dictionary<string, SkinnedMeshRenderer> Renderers =
            new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);

        public string Name { get; }
        private readonly string designId;
        private readonly Dictionary<string, float> normalStrengths;

        public CombatAuthoredBodyBank(string name, string expectedDesignId, string[] roles, float[] strengths)
        {
            if (roles == null || strengths == null || roles.Length != 3 || strengths.Length != roles.Length)
                throw new ArgumentException("An authored body requires three bounded shared surface roles.");
            Name = name; designId = expectedDesignId;
            normalStrengths = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int i = 0; i < roles.Length; i++) normalStrengths.Add(roles[i], strengths[i]);
        }

        public BodyManifest LoadBodyManifest()
        {
            if (manifest != null) return manifest;
            TextAsset source = Resources.Load<TextAsset>("Combat/" + Name + "Body")
                ?? throw new InvalidOperationException("Missing authored body manifest.");
            BodyManifest value = JsonUtility.FromJson<BodyManifest>(source.text);
            if (value == null || value.design_id != designId || value.rig != "NpcHumanV2" ||
                value.geometry_owner != "independent_sculpt" || !value.retained_rest_skeleton ||
                value.bounds_space != "unity_metres" || value.uv_channel != 0 || value.source_uv_channel != 1 ||
                value.meshes == null || value.meshes.Length == 0 || value.meshes.Length > 128 ||
                value.textures == null || value.textures.Length != 2 ||
                value.estimated_gpu_bytes <= 0 || value.estimated_gpu_bytes > 7L * 1024 * 1024)
                throw new InvalidOperationException("Authored body violates its bounded retained-skeleton contract.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyMesh mesh in value.meshes)
            {
                if (mesh == null || string.IsNullOrWhiteSpace(mesh.name) || !mesh.name.StartsWith("GEO_", StringComparison.Ordinal) ||
                    !names.Add(mesh.name) || mesh.surfaces == null || mesh.surfaces.Length == 0 || mesh.surfaces.Length > 3 ||
                    mesh.bounds_min == null || mesh.bounds_min.Length != 3 || mesh.bounds_max == null || mesh.bounds_max.Length != 3)
                    throw new InvalidOperationException("Authored body requires unique original anatomical renderer names.");
                foreach (string role in mesh.surfaces)
                    if (!normalStrengths.ContainsKey(role))
                        throw new InvalidOperationException("Unknown authored surface role: " + role);
                for (int axis = 0; axis < 3; axis++)
                    if (!float.IsFinite(mesh.bounds_min[axis]) || !float.IsFinite(mesh.bounds_max[axis]) || mesh.bounds_min[axis] > mesh.bounds_max[axis])
                        throw new InvalidOperationException("Authored body requires finite authored bounds: " + mesh.name);
            }
            names.Clear();
            foreach (BodyTexture texture in value.textures)
            {
                bool isAtlas = texture != null && texture.name == Name + "BodyAtlas";
                if (texture == null || !names.Add(texture.name) || !isAtlas && texture.name != Name + "MaterialResponse" ||
                    texture.size != (isAtlas ? 2048 : 1024) || texture.sRGB != isAtlas ||
                    string.IsNullOrEmpty(texture.sha256) || texture.sha256.Length != 64)
                    throw new InvalidOperationException("Authored body requires its own albedo and linear material response.");
            }
            manifest = value;
            return value;
        }

        public SkinnedMeshRenderer LoadBodyRenderer(string name)
        {
            if (body == null)
            {
                body = Resources.Load<GameObject>("Combat/" + Name + "Body")
                    ?? throw new InvalidOperationException("Missing authored body skin.");
                foreach (SkinnedMeshRenderer renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (!Renderers.TryAdd(renderer.name, renderer))
                        throw new InvalidOperationException("Repeated Authored body renderer: " + renderer.name);
                foreach (BodyMesh mesh in LoadBodyManifest().meshes)
                    if (!Renderers.ContainsKey(mesh.name))
                        throw new InvalidOperationException("Missing Authored body renderer: " + mesh.name);
            }
            return Renderers.TryGetValue(name, out SkinnedMeshRenderer result) ? result : null;
        }

        public Texture2D LoadBodyAtlas() => atlas != null ? atlas : atlas =
            Resources.Load<Texture2D>("Combat/" + Name + "BodyAtlas") ?? throw new InvalidOperationException("Missing Authored body albedo.");

        public Material LoadBodyMaterial(string surface = "flesh")
        {
            if (!normalStrengths.TryGetValue(surface, out float strength))
                throw new ArgumentOutOfRangeException(nameof(surface), surface, "Unknown authored body surface.");
            if (!materials.TryGetValue(surface, out Material material) || material == null)
                materials[surface] = material = CreateMaterial(Name + " " + surface + " Shared", strength);
            return material;
        }

        private Material CreateMaterial(string name, float normalStrength)
        {
            Shader shader = Resources.Load<Shader>("Shaders/WarehouseSurface")
                ?? throw new InvalidOperationException("Missing shared authored surface shader.");
            response ??= Resources.Load<Texture2D>("Combat/" + Name + "MaterialResponse")
                ?? throw new InvalidOperationException("Missing Authored material response.");
            var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            material.SetTexture("_BaseMap", LoadBodyAtlas());
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_WarehouseMaterialResponse", response);
            material.SetFloat("_WarehouseResponseStrength", 1f);
            material.SetFloat("_WarehouseNormalStrength", normalStrength);
            material.SetFloat("_WarehouseAuxiliary", 0f);
            material.SetFloat("_Smoothness", .12f);
            material.SetFloat("_Metallic", 0f);
            return material;
        }

        public void Clear()
        {
            foreach (Material material in materials.Values)
                if (material != null) UnityEngine.Object.Destroy(material);
            materials.Clear(); manifest = null; body = null; atlas = response = null; Renderers.Clear();
        }
    }
}
