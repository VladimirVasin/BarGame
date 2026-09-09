using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [Serializable] public sealed class VillageWorkroomPart
    {
        public string name, mesh, role, surface, parent;
        public float[] position, euler, scale, tint, bounds_min, bounds_max;
        public bool solid;
        public int triangles;
        public VillageLifePropAnchor[] anchors;
    }
    [Serializable] public sealed class VillageWorkroomManifest
    {
        public string generator_version, design_id, scale_mode, build_signature, house_id;
        public int mesh_count, triangle_count;
        public float[] room_min, room_max;
        public VillageLifePropAnchor[] anchors;
        public VillageWorkroomPart[] parts;
    }

    /// <summary>Passive fixed-metre parts. People, repair progress, lights and weather belong to the room controller.</summary>
    public static class VillageWorkroomAssets
    {
        public const string ResourcePath = "VillageLife/VillageWorkroom3D";
        public const string DesignId = "village_workroom_08_v1", GeneratorVersion = "1.0.0";
        private static VillageWorkroomManifest manifest;
        private static Dictionary<string, MeshFilter> meshes;
        public static VillageWorkroomManifest Manifest { get { Load(); return manifest; } }
        private static void Load()
        {
            if (manifest != null && meshes != null) return;
            var text = Resources.Load<TextAsset>(ResourcePath);
            var model = Resources.Load<GameObject>(ResourcePath);
            if (text == null || model == null) throw new InvalidOperationException("Missing authored village workroom.");
            var data = JsonUtility.FromJson<VillageWorkroomManifest>(text.text);
            if (data == null || data.design_id != DesignId || data.generator_version != GeneratorVersion ||
                data.scale_mode != "fixed_metres" || data.house_id != VillageWorkroomPlan.HouseId ||
                data.parts == null || data.parts.Length != data.mesh_count || data.anchors == null)
                throw new InvalidOperationException("Invalid village workroom manifest.");
            var found = new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == null || !found.TryAdd(filter.sharedMesh.name, filter))
                    throw new InvalidOperationException("Duplicate workroom source mesh.");
            if (found.Count != data.mesh_count) throw new InvalidOperationException("Workroom mesh count drifted.");
            manifest = data; meshes = found;
        }
        public static bool TryGetShellPart(VillageMeshRole role, out MeshFilter filter)
        {
            Load();
            return meshes.TryGetValue("GEO_Workroom_Shell" + role, out filter);
        }
        public static VillageWorkroomInstance Create(Transform houseRoot, AlpineVillagePlotDescriptor plot)
        {
            Load();
            var root = new GameObject("Village Workroom 08"); root.transform.SetParent(houseRoot, false);
            var instance = root.AddComponent<VillageWorkroomInstance>();
            var parts = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var anchors = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var solids = new List<Collider>();
            foreach (VillageWorkroomPart part in manifest.parts)
            {
                if (part.name.StartsWith("Shell", StringComparison.Ordinal)) continue;
                var host = new GameObject(part.name);
                host.transform.SetParent(string.IsNullOrEmpty(part.parent) ? root.transform : parts[part.parent], false);
                host.transform.localPosition = V(part.position); host.transform.localRotation = Quaternion.Euler(V(part.euler));
                host.transform.localScale = part.scale == null ? Vector3.one : V(part.scale);
                parts.Add(part.name, host.transform);
                MeshFilter template = meshes[part.mesh];
                var meshObject = new GameObject("Geometry"); meshObject.transform.SetParent(host.transform, false);
                meshObject.transform.localPosition = template.transform.position;
                meshObject.transform.localRotation = template.transform.rotation;
                meshObject.transform.localScale = template.transform.lossyScale;
                meshObject.AddComponent<MeshFilter>().sharedMesh = template.sharedMesh;
                var renderer = meshObject.AddComponent<MeshRenderer>();
                ApplySurface(renderer, part);
                if (part.solid)
                {
                    var solid = meshObject.AddComponent<MeshCollider>(); solid.sharedMesh = template.sharedMesh;
                    solids.Add(solid);
                }
                foreach (VillageLifePropAnchor anchor in part.anchors)
                {
                    var child = new GameObject("ANCHOR_" + anchor.name); child.transform.SetParent(host.transform, false);
                    child.transform.localPosition = V(anchor.position);
                }
            }
            foreach (VillageLifePropAnchor anchor in manifest.anchors)
            {
                var child = new GameObject("ANCHOR_" + anchor.name); child.transform.SetParent(root.transform, false);
                child.transform.localPosition = V(anchor.position); anchors.Add(anchor.name, child.transform);
            }
            instance.Initialize(houseRoot, new VillageWorkroomPlan(plot), parts, anchors, solids);
            return instance;
        }
        private static void ApplySurface(MeshRenderer renderer, VillageWorkroomPart part)
        {
            var tint = new Color(part.tint[0], part.tint[1], part.tint[2], part.tint[3]);
            var block = new MaterialPropertyBlock();
            if (part.surface == "Glass")
            {
                renderer.sharedMaterial = HomeBalconyResources.GlassMaterial;
                block.SetColor("_BaseColor", tint); renderer.SetPropertyBlock(block); return;
            }
            if (part.surface == "Cloth")
            {
                const CityPointOfInterestSurfaceKind cloth = CityPointOfInterestSurfaceKind.Cloth;
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                block.SetTexture("_BaseMap", CityPointOfInterestSurfaceAppearance.GetTexture(cloth));
                Color display = CityPointOfInterestSurfaceAppearance.CreateDisplayTint(tint, cloth);
                block.SetColor("_BaseColor", display); block.SetColor("_Color", display);
                float pitch = 1f / CityPointOfInterestSurfaceAppearance.GetRecipe(cloth).MetersPerTile;
                block.SetVector("_BaseMap_ST", new Vector4(pitch, pitch, 0f, 0f));
                renderer.SetPropertyBlock(block); return;
            }
            if (part.surface == "Lamp")
            {
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                block.SetColor("_BaseColor", tint); block.SetColor("_Color", tint);
                renderer.SetPropertyBlock(block); return;
            }
            if (!Enum.TryParse(part.surface, out MountainRoadSurfaceKind surface))
                throw new InvalidOperationException("Unknown workroom surface " + part.surface);
            VillageFacadeAppearance.Apply(renderer, surface, tint, verticalTimber: true);
            renderer.GetPropertyBlock(block);
            // These authored UVs are already metres, independent of the FBX's 100x root factor.
            float tile = surface == MountainRoadSurfaceKind.Timber ? 1.4f :
                surface == MountainRoadSurfaceKind.Masonry || surface == MountainRoadSurfaceKind.LayeredStone ? 2.4f :
                MountainRoadSurfaceAppearance.GetRecipe(surface).MetersPerTile;
            block.SetVector("_BaseMap_ST", new Vector4(1f / tile, 1f / tile, 0f, 0f)); renderer.SetPropertyBlock(block);
        }
        private static Vector3 V(float[] a) => VillageLifePropLibrary.Vector(a);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { manifest = null; meshes = null; }
    }

    [DisallowMultipleComponent]
    public sealed class VillageWorkroomInstance : MonoBehaviour
    {
        public Transform Root => transform;
        public Transform HouseRoot { get; private set; }
        public VillageWorkroomPlan Plan { get; private set; }
        public IReadOnlyDictionary<string, Transform> Parts { get; private set; }
        public IReadOnlyDictionary<string, Transform> Anchors { get; private set; }
        public IReadOnlyList<Collider> SolidColliders { get; private set; }
        internal void Initialize(Transform house, VillageWorkroomPlan plan, Dictionary<string, Transform> parts,
            Dictionary<string, Transform> anchors, List<Collider> solids)
        {
            HouseRoot = house; Plan = plan; Parts = parts; Anchors = anchors; SolidColliders = solids.AsReadOnly();
        }
        public Transform Part(string name) => Parts.TryGetValue(name, out Transform part) ? part :
            throw new ArgumentException("Unknown workroom part " + name);
    }
}
