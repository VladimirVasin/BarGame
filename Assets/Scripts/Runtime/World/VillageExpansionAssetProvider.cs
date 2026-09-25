using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class VillageExpansionPart
    {
        public string kind, name, mesh, surface, parent;
        public bool solid, hidden;
        public float[] tint, bounds_min, bounds_max;
        public string terrain_fit;
        public float[] support;
        public int triangles;
        public int flame_field_vertex_count;
    }

    [Serializable]
    public sealed class VillageExpansionAnchor
    {
        public string kind, name, parent;
        public float[] position;
    }

    [Serializable]
    public sealed class VillageExpansionManifest
    {
        public string generator_version, design_id, scale_mode, uv_mode, build_signature;
        public int mesh_count, triangle_count, animation_count;
        public bool colliders, lights, cameras;
        public VillageExpansionPart[] parts;
        public VillageExpansionAnchor[] anchors;
        public float[] avalanche_origin, avalanche_footprint;
    }

    /// <summary>Passive Blender geometry, retaining the measured imported metre scale.
    /// Placement and blocking footprints belong to AlpineVillageExpansionPlan.</summary>
    public sealed class VillageExpansionAssetProvider
    {
        public const string ResourcePath = "Village/Expansion/VillageExpansion3D";
        public const string DesignId = "village_forest_ski_base_old_road_v1";
        public const string GeneratorVersion = "1.10.0";
        public const string WreckRustTexturePath = "Village/Textures/VillageTruckRustAlbedo";
        public const string WreckPaintTexturePath = "Village/Textures/VillageTruckPaintAlbedo";
        public const string LodgePicturesTexturePath = "Village/Textures/LodgePictures";
        public const string LodgeGroupPhotographTexturePath = "Village/Textures/LodgeGroupPhotograph";
        private static VillageExpansionAssetProvider instance;
        private static Texture2D wreckRust, wreckPaint, lodgePictures, lodgeGroupPhotograph;
        private static Material flameMaterial;
        private static readonly Dictionary<string, Texture2D> agedTextures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, MeshFilter> meshes;
        public VillageExpansionManifest Manifest { get; }

        private VillageExpansionAssetProvider(GameObject model, VillageExpansionManifest manifest)
        {
            Manifest = manifest;
            meshes = new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == null || !meshes.TryAdd(filter.sharedMesh.name, filter))
                    throw new InvalidOperationException("Duplicate or missing village expansion mesh.");
            if (meshes.Count != manifest.mesh_count)
                throw new InvalidOperationException("Village expansion mesh count differs from its manifest.");
        }

        public static VillageExpansionManifest ParseManifestOrThrow(string json)
        {
            var value = JsonUtility.FromJson<VillageExpansionManifest>(json);
            if (value == null || value.generator_version != GeneratorVersion || value.design_id != DesignId ||
                value.scale_mode != "fixed_metres" || value.uv_mode != "projected_metres" ||
                string.IsNullOrEmpty(value.build_signature) || value.parts == null ||
                value.parts.Length != value.mesh_count || value.mesh_count == 0 || value.colliders ||
                value.lights || value.cameras || value.animation_count != 0 || value.anchors == null)
                throw new InvalidOperationException("Invalid or stale passive village expansion manifest.");
            if (value.avalanche_origin == null || value.avalanche_origin.Length != 2 ||
                Vector2.Distance(new Vector2(value.avalanche_origin[0], value.avalanche_origin[1]),
                    AlpineVillageAvalanchePlan.Origin) > .0001f || value.avalanche_footprint == null ||
                value.avalanche_footprint.Length != AlpineVillageAvalanchePlan.Footprint.Count * 2)
                throw new InvalidOperationException("Avalanche authoring and placement origins differ.");
            for (int i = 0; i < AlpineVillageAvalanchePlan.Footprint.Count; i++)
                if (Vector2.Distance(AlpineVillageAvalanchePlan.Footprint[i],
                    new Vector2(value.avalanche_footprint[i * 2], value.avalanche_footprint[i * 2 + 1])) > .0001f)
                    throw new InvalidOperationException("Avalanche mesh and movement outlines differ.");
            foreach (VillageExpansionPart part in value.parts)
                if (part.kind == "Avalanche" &&
                    (part.terrain_fit != "surface" && part.terrain_fit != "rigid" ||
                     part.terrain_fit == "rigid" && (part.support == null || part.support.Length != 3)))
                    throw new InvalidOperationException("Avalanche part has no terrain fitting contract.");
            var anchors = new HashSet<string>(StringComparer.Ordinal);
            foreach (VillageExpansionAnchor anchor in value.anchors)
                if (string.IsNullOrEmpty(anchor.kind) || string.IsNullOrEmpty(anchor.name) ||
                    anchor.position == null || anchor.position.Length != 3 ||
                    !anchors.Add(anchor.kind + "/" + anchor.name))
                    throw new InvalidOperationException("Invalid village expansion interaction anchor.");
            return value;
        }

        public static VillageExpansionAssetProvider LoadOrThrow()
        {
            if (instance != null) return instance;
            var model = Resources.Load<GameObject>(ResourcePath);
            var text = Resources.Load<TextAsset>(ResourcePath);
            if (model == null || text == null)
                throw new InvalidOperationException("Missing authored village expansion pack: " + ResourcePath);
            return instance = new VillageExpansionAssetProvider(model, ParseManifestOrThrow(text.text));
        }

        public GameObject Create(string kind, string name, Transform parent, Vector3 position,
            Quaternion rotation, Vector3? scale = null)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, rotation);
            Vector3 placementScale = scale ?? Vector3.one;
            root.transform.localScale = placementScale;
            int count = 0;
            foreach (VillageExpansionPart part in Manifest.parts)
            {
                if (part.kind != kind) continue;
                MeshFilter source = meshes[part.mesh];
                var child = new GameObject(part.name);
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = source.transform.position;
                child.transform.localRotation = source.transform.rotation;
                child.transform.localScale = source.transform.lossyScale;
                child.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                var renderer = child.AddComponent<MeshRenderer>();
                ApplySurface(renderer, part, placementScale);
                if (part.solid) child.AddComponent<MeshCollider>().sharedMesh = source.sharedMesh;
                renderer.enabled = !part.hidden;
                count++;
            }
            if (count == 0) throw new InvalidOperationException("Missing village expansion kind " + kind);
            // Both dock positions and geometry are authored in metres, independently
            // of the imported FBX root's retained 100x scale.
            foreach (VillageExpansionAnchor anchor in Manifest.anchors)
            {
                if (anchor.kind != kind) continue;
                var child = new GameObject(anchor.name);
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = new Vector3(anchor.position[0], anchor.position[1], anchor.position[2]);
            }
            foreach (VillageExpansionPart part in Manifest.parts)
                if (part.kind == kind && !string.IsNullOrEmpty(part.parent))
                    root.transform.Find(part.name).SetParent(root.transform.Find(part.parent), true);
            foreach (VillageExpansionAnchor anchor in Manifest.anchors)
                if (anchor.kind == kind && !string.IsNullOrEmpty(anchor.parent))
                    root.transform.Find(anchor.name).SetParent(root.transform.Find(anchor.parent), true);
            return root;
        }

        /// <summary>Reuses an authored detachable prop without constructing its surrounding building.</summary>
        public GameObject CreateAnchoredProp(string kind, string anchorName, string name, Transform parent)
        {
            VillageExpansionAnchor anchor = Array.Find(Manifest.anchors,
                candidate => candidate.kind == kind && candidate.name == anchorName);
            if (anchor == null) throw new InvalidOperationException("Missing expansion prop anchor: " + anchorName);
            var origin = new Vector3(anchor.position[0], anchor.position[1], anchor.position[2]);
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            int count = 0;
            foreach (VillageExpansionPart part in Manifest.parts)
            {
                if (part.kind != kind || part.parent != anchorName) continue;
                MeshFilter source = meshes[part.mesh];
                var child = new GameObject(part.name);
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = source.transform.position - origin;
                child.transform.localRotation = source.transform.rotation;
                child.transform.localScale = source.transform.lossyScale;
                child.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                ApplySurface(child.AddComponent<MeshRenderer>(), part, Vector3.one);
                count++;
            }
            if (count == 0)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(root);
                else UnityEngine.Object.DestroyImmediate(root);
                throw new InvalidOperationException("Empty authored expansion prop: " + anchorName);
            }
            return root;
        }

        private static void ApplySurface(MeshRenderer renderer, VillageExpansionPart part, Vector3 scale)
        {
            var tint = new Color(part.tint[0], part.tint[1], part.tint[2], part.tint[3]);
            var block = new MaterialPropertyBlock();
            if (part.surface == "LodgePictures" || part.surface == "LodgeGroupPhotograph")
            {
                bool groupPhotograph = part.surface == "LodgeGroupPhotograph";
                if (groupPhotograph && lodgeGroupPhotograph == null)
                    lodgeGroupPhotograph = Resources.Load<Texture2D>(LodgeGroupPhotographTexturePath);
                if (!groupPhotograph && lodgePictures == null)
                    lodgePictures = Resources.Load<Texture2D>(LodgePicturesTexturePath);
                Texture2D picture = groupPhotograph ? lodgeGroupPhotograph : lodgePictures;
                if (picture == null) throw new InvalidOperationException("Missing lodge picture: " + part.surface);
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                block.SetTexture("_BaseMap", picture);
                block.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
                block.SetColor("_BaseColor", Color.white);
                block.SetColor("_Color", Color.white);
                block.SetFloat("_Smoothness", 0f);
                block.SetFloat("_Metallic", 0f);
                renderer.SetPropertyBlock(block);
                return;
            }
            if (part.surface == "Fire")
            {
                if (flameMaterial == null)
                    flameMaterial = Resources.Load<Material>("Materials/MothersHouseFlame");
                if (flameMaterial == null) throw new InvalidOperationException("Missing shared thermal flame material.");
                renderer.sharedMaterial = flameMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                block.SetColor("_BaseColor", tint);
                block.SetFloat("_FireSway", part.kind == "Lighter" ? .0015f : .018f);
                renderer.SetPropertyBlock(block);
                return;
            }
            if (part.surface == "LighterMetal")
            {
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                block.SetFloat("_Metallic", .72f);
                block.SetFloat("_Smoothness", .32f);
                renderer.SetPropertyBlock(block);
                return;
            }
            if (part.surface == "DarkWindow" || part.surface.StartsWith("Abandoned", StringComparison.Ordinal))
            {
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                if (part.surface != "DarkWindow")
                {
                    if (!agedTextures.TryGetValue(part.surface, out Texture2D texture))
                    {
                        texture = Resources.Load<Texture2D>("Village/Textures/" + part.surface);
                        if (texture == null) throw new InvalidOperationException("Missing aged village surface " + part.surface);
                        agedTextures.Add(part.surface, texture);
                    }
                    block.SetTexture("_BaseMap", texture);
                    tint = new Color(Compensate(tint.r), Compensate(tint.g), Compensate(tint.b), tint.a);
                }
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                block.SetColor("_EmissionColor", Color.black);
                block.SetFloat("_Smoothness", .02f);
                block.SetFloat("_Metallic", 0f);
                float agedPitch = part.surface == "AbandonedWood" ? 1.4f : 2.4f;
                block.SetVector("_BaseMap_ST", new Vector4(scale.x / agedPitch, scale.y / agedPitch, 0f, 0f));
                renderer.SetPropertyBlock(block);
                return;
            }
            if (part.surface == "WreckRust" || part.surface == "WreckPaint")
            {
                bool paint = part.surface == "WreckPaint";
                if (paint && wreckPaint == null) wreckPaint = Resources.Load<Texture2D>(WreckPaintTexturePath);
                if (!paint && wreckRust == null) wreckRust = Resources.Load<Texture2D>(WreckRustTexturePath);
                Texture2D texture = paint ? wreckPaint : wreckRust;
                if (texture == null) throw new InvalidOperationException("Missing truck wreck albedo: " + part.surface);
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                block.SetTexture("_BaseMap", texture);
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                block.SetFloat("_Smoothness", .04f);
                block.SetFloat("_Metallic", .02f);
                block.SetVector("_BaseMap_ST", new Vector4(scale.x / 1.65f, scale.y / 1.65f, 0f, 0f));
                renderer.SetPropertyBlock(block);
                return;
            }
            if (part.surface == "Canvas")
            {
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                CityPointOfInterestSurfaceAppearance.ApplyClothPanel(renderer, tint, 1f, 1f);
                return;
            }
            if (part.surface == "Glass")
            {
                renderer.sharedMaterial = HomeBalconyResources.GlassMaterial;
                block.SetColor("_BaseColor", tint);
                renderer.SetPropertyBlock(block);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return;
            }
            if (!Enum.TryParse(part.surface, out MountainRoadSurfaceKind surface))
                throw new InvalidOperationException("Unknown expansion surface " + part.surface);
            VillageFacadeAppearance.Apply(renderer, surface, tint,
                verticalTimber: part.name != "Roof", roofTimber: part.name == "Roof");
            renderer.GetPropertyBlock(block);
            float pitch = surface == MountainRoadSurfaceKind.Timber ? 1.4f :
                surface == MountainRoadSurfaceKind.Masonry || surface == MountainRoadSurfaceKind.LayeredStone ? 2.4f :
                MountainRoadSurfaceAppearance.GetRecipe(surface).MetersPerTile;
            // FBX vertices retain metre UVs despite the author's 100x import root.
            // Asphalt strips alone scale in X/Z, so preserve their physical texture pitch.
            block.SetVector("_BaseMap_ST", new Vector4(scale.x / pitch,
                (part.kind == "RoadSurface" ? scale.z : scale.y) / pitch, 0f, 0f));
            renderer.SetPropertyBlock(block);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            instance = null;
            wreckRust = wreckPaint = lodgePictures = lodgeGroupPhotograph = null;
            flameMaterial = null;
            agedTextures.Clear();
        }

        private static float Compensate(float gamma) =>
            Mathf.LinearToGammaSpace(Mathf.Clamp01(Mathf.GammaToLinearSpace(gamma) / .58f));
    }
}
