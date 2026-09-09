using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum VillageLifePropKind
    {
        WoodShelter, LogStack, Log, Basket, BasketStand, ChoppingBlock,
        Axe, UtilitySled, StationCrate, StationLid, StationStrap,
        Shovel, ClosedBasket, GatePosts, GateLeaf, ShovelRack, PorchMat
    }

    [Serializable]
    public sealed class VillageLifePropAnchor
    {
        public string name;
        public float[] position;
    }

    [Serializable]
    public sealed class VillageLifePropPart
    {
        public string mesh;
        public string role;
        public string surface;
        public float[] tint;
        public float[] bounds_min;
        public float[] bounds_max;
        public int triangles;
    }

    [Serializable]
    public sealed class VillageLifePropDefinition
    {
        public string kind;
        public string origin;
        public float[] bounds_min;
        public float[] bounds_max;
        public VillageLifePropAnchor[] anchors;
        public VillageLifePropPart[] parts;
    }

    [Serializable]
    public sealed class VillageLifePropManifest
    {
        public string generator_version;
        public string design_id;
        public string build_signature;
        public string stage1_signature;
        public string scale_mode;
        public string uv_mode;
        public bool colliders;
        public bool lights;
        public bool cameras;
        public int animation_count;
        public int prop_count;
        public int mesh_count;
        public int triangle_count;
        public VillageLifePropDefinition[] props;
    }

    /// <summary>Passive Blender models in working metres. Placement, collision,
    /// grip ownership and animation belong to the life builder/controller.</summary>
    public static class VillageLifePropLibrary
    {
        public const string ResourcePath = "VillageLife/VillageLifeProps3D";
        public const string GeneratorVersion = "1.1.0";
        public const string DesignId = "village_life_work_props_v1";
        public const int ExpectedPropCount = 17;
        public const string Stage1Signature = "5ebf8898e36129174945a3f8e01f7a09c23bfca220e137c3efc3bcc32940eabf";
        private static VillageLifePropManifest manifest;
        private static Dictionary<string, MeshFilter> meshes;

        public static VillageLifePropManifest Manifest
        {
            get { LoadOrThrow(); return manifest; }
        }

        public static void LoadOrThrow()
        {
            if (manifest != null && meshes != null) return;
            var source = Resources.Load<GameObject>(ResourcePath);
            var text = Resources.Load<TextAsset>(ResourcePath);
            if (source == null || text == null)
                throw new InvalidOperationException("Missing generated Village Life props FBX/manifest.");
            VillageLifePropManifest loaded = JsonUtility.FromJson<VillageLifePropManifest>(text.text);
            if (loaded == null || loaded.design_id != DesignId ||
                loaded.generator_version != GeneratorVersion || loaded.scale_mode != "fixed_metres" ||
                loaded.prop_count != ExpectedPropCount || loaded.props == null || loaded.props.Length != ExpectedPropCount ||
                loaded.stage1_signature != Stage1Signature ||
                loaded.colliders || loaded.lights || loaded.cameras || loaded.animation_count != 0)
                throw new InvalidOperationException("Village Life prop manifest violates its passive metre contract.");
            var found = new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
            foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !found.TryAdd(filter.sharedMesh.name, filter))
                    throw new InvalidOperationException("Missing or duplicate Village Life prop mesh.");
            }
            if (found.Count != loaded.mesh_count)
                throw new InvalidOperationException("Village Life FBX/manifest mesh counts differ.");
            foreach (VillageLifePropDefinition definition in loaded.props)
                foreach (VillageLifePropPart part in definition.parts)
                    if (!found.ContainsKey(part.mesh))
                        throw new InvalidOperationException("Missing authored Village Life mesh: " + part.mesh);
            meshes = found;
            manifest = loaded;
        }

        public static VillageLifePropDefinition GetDefinition(VillageLifePropKind kind)
        {
            LoadOrThrow();
            foreach (VillageLifePropDefinition definition in manifest.props)
                if (definition.kind == kind.ToString()) return definition;
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        public static Bounds GetBounds(VillageLifePropKind kind)
        {
            VillageLifePropDefinition definition = GetDefinition(kind);
            var bounds = new Bounds();
            bounds.SetMinMax(Vector(definition.bounds_min), Vector(definition.bounds_max));
            return bounds;
        }

        public static Vector3 GetAnchor(VillageLifePropKind kind, string name)
        {
            foreach (VillageLifePropAnchor anchor in GetDefinition(kind).anchors)
                if (anchor.name == name) return Vector(anchor.position);
            throw new InvalidOperationException($"Village Life {kind} has no anchor '{name}'.");
        }

        public static GameObject Create(VillageLifePropKind kind, Transform parent, string name = null)
        {
            VillageLifePropDefinition definition = GetDefinition(kind);
            var root = new GameObject(name ?? kind.ToString());
            root.transform.SetParent(parent, false);
            foreach (VillageLifePropPart part in definition.parts)
            {
                MeshFilter template = meshes[part.mesh];
                var host = new GameObject(part.mesh);
                host.transform.SetParent(root.transform, false);
                // The imported FBX stores the metre factor on its hierarchy.
                // Use the complete authored transform, never a unit mesh clone.
                host.transform.localPosition = template.transform.position;
                host.transform.localRotation = template.transform.rotation;
                host.transform.localScale = template.transform.lossyScale;
                host.AddComponent<MeshFilter>().sharedMesh = template.sharedMesh;
                MeshRenderer renderer = host.AddComponent<MeshRenderer>();
                ApplySurface(renderer, part);
            }
            foreach (VillageLifePropAnchor anchor in definition.anchors)
            {
                var host = new GameObject("ANCHOR_" + anchor.name);
                host.transform.SetParent(root.transform, false);
                host.transform.localPosition = Vector(anchor.position);
            }
            return root;
        }

        private static void ApplySurface(MeshRenderer renderer, VillageLifePropPart part)
        {
            var tint = new Color(part.tint[0], part.tint[1], part.tint[2], part.tint[3]);
            if (part.surface == "Cloth")
            {
                const CityPointOfInterestSurfaceKind cloth = CityPointOfInterestSurfaceKind.Cloth;
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                var block = new MaterialPropertyBlock();
                block.SetTexture("_BaseMap", CityPointOfInterestSurfaceAppearance.GetTexture(cloth));
                Color display = CityPointOfInterestSurfaceAppearance.CreateDisplayTint(tint, cloth);
                block.SetColor("_BaseColor", display);
                block.SetColor("_Color", display);
                float pitch = 1f / CityPointOfInterestSurfaceAppearance.GetRecipe(cloth).MetersPerTile;
                block.SetVector("_BaseMap_ST", new Vector4(pitch, pitch, 0f, 0f));
                block.SetFloat("_Smoothness", 0f);
                block.SetFloat("_Metallic", 0f);
                renderer.SetPropertyBlock(block);
                return;
            }
            if (!Enum.TryParse(part.surface, out MountainRoadSurfaceKind surface))
                throw new InvalidOperationException("Unknown Village Life surface: " + part.surface);
            MountainRoadSurfaceAppearance.ApplyCombined(renderer, surface, tint);
            float uvScale = 1f / MountainRoadSurfaceAppearance.GetRecipe(surface).MetersPerTile;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetVector("_BaseMap_ST", new Vector4(uvScale, uvScale, 0f, 0f));
            renderer.SetPropertyBlock(properties);
        }

        public static Vector3 Vector(float[] values) => new Vector3(values[0], values[1], values[2]);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            manifest = null;
            meshes = null;
        }
    }
}
