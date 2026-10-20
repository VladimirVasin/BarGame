using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Imported fixed-metre warehouse kit. Unit wrappers retain every FBX scale and axis factor.</summary>
    public static class WarehouseArenaAssetProvider
    {
        public const string ResourceFolder = "CombatWarehouse/";
        public static readonly string[] ModelNames = { "Shell", "Platform", "Stair", "Rail", "Rack", "Cargo", "Pipes", "Lamp",
            "PipeStraight", "PipeElbow", "PipeEnd", "RoomShell", "MainDoor", "Gate", "FanHousing", "FanRotor",
            "LiftFrame", "LiftPlatform", "ChainDrum", "PusherFrame", "PusherHead", "LockerProps", "BreakProps",
            "TechnicalProps", "TwistedFrame", "GateControl", "EmergencyLamp",
            "RoomSign1", "RoomSign2", "RoomSign3", "RoomSign4", "RoomSign5", "RoomSign6", "RoomSign7", "RoomSign8", "RoomSign9",
            "FishPreparationStation", "EmergencyRoomLamp", "RottenFishTailCan", "RottenFishBellyCan", "RottenFishHeadCan", "FishStationLamp" };
        public static readonly string[] FishModelNames = { "FishPreparationStation", "EmergencyRoomLamp",
            "RottenFishTailCan", "RottenFishBellyCan", "RottenFishHeadCan", "FishStationLamp" };
        public const int CoreModelCount = 36;
        public static readonly string[] TextureNames = { "WarehouseMasterAtlas", "WarehouseMaterialResponse",
            "WarehouseWearDecals", "WarehouseFishLabels" };
        public static readonly string[] AtlasTextureNames = { "WarehouseMasterAtlas" };
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();
        private static readonly HashSet<string> Measured = new HashSet<string>();
        private static Manifest manifest;
        private static Manifest fishManifest;
        private static readonly Dictionary<string, Dictionary<int, Dictionary<string, Mesh>>> SurfaceVariants =
            new Dictionary<string, Dictionary<int, Dictionary<string, Mesh>>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Vector4> AuxiliaryRects = new Dictionary<string, Vector4>(StringComparer.Ordinal);
        private static bool variantsLoaded;

        [Serializable] private sealed class Manifest { public Model[] models; }
        [Serializable] private sealed class Model { public string name; public float[] bounds_min, bounds_max; }
        [Serializable] private sealed class SurfaceVariantManifest { public SurfaceVariant[] variants; public AuxiliarySurface[] auxiliary; }
        [Serializable] private sealed class SurfaceVariant { public string kind; public int slot; public SurfaceMesh[] meshes; }
        [Serializable] private sealed class SurfaceMesh { public string source, node; }
        [Serializable] private sealed class AuxiliarySurface { public string role; public float[] rect; }

        public static GameObject Create(string kind, Transform parent, Vector3 position, Quaternion rotation, int surfaceSlot = 0)
        {
            if (Array.IndexOf(ModelNames,kind) < 0) throw new ArgumentOutOfRangeException(nameof(kind));
            int slotCount = SurfaceSlotCount(kind);
            if (surfaceSlot < 0 || surfaceSlot >= slotCount) throw new ArgumentOutOfRangeException(nameof(surfaceSlot));
            Dictionary<string, Mesh> surfaceMeshes = null;
            if (SurfaceVariants.TryGetValue(kind, out Dictionary<int, Dictionary<string, Mesh>> slots))
                slots.TryGetValue(surfaceSlot, out surfaceMeshes);
            if (!Templates.TryGetValue(kind,out GameObject template) || template == null)
            {
                template = Resources.Load<GameObject>(ResourceFolder + kind);
                if (template == null) throw new InvalidOperationException("Missing warehouse model " + kind);
                Templates[kind] = template;
            }
            var wrapper = new GameObject("Warehouse " + kind);
            wrapper.transform.SetParent(parent,false);
            // Never set this imported child's localScale: its root owns the FBX centimetre factor.
            UnityEngine.Object.Instantiate(template,wrapper.transform,false);
            foreach (MeshRenderer renderer in wrapper.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool collision = renderer.name.StartsWith("Collision_",StringComparison.Ordinal);
                if (collision)
                {
                    renderer.enabled = false;
                    MeshCollider collider = renderer.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    FootstepGround.Stamp(collider,kind == "Platform" || kind == "Stair" ?
                        FootstepGroundKind.Metal : FootstepGroundKind.Concrete);
                    continue;
                }
                if (surfaceMeshes != null && surfaceMeshes.TryGetValue(SurfaceSourceName(renderer.name), out Mesh variant))
                    renderer.GetComponent<MeshFilter>().sharedMesh = variant;
                int separator = renderer.name.LastIndexOf("__",StringComparison.Ordinal);
                if (separator < 0) throw new InvalidOperationException("Warehouse mesh lacks a surface role: " + renderer.name);
                string role = SurfaceSourceName(renderer.name.Substring(separator + 2));
                renderer.sharedMaterial = Surface(kind,role);
                renderer.shadowCastingMode = role == "Lamp" || role == "SignalRed" || role == "SignalRedOff" || role == "Glass" || role == "WearDecal" ?
                    ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                // Only static visual nodes enter the optional environment cube.
                // Imported collision nodes above retain their original physics layer.
                if (IsReflectionArchitecture(kind))
                    renderer.gameObject.layer = WarehouseReflectionController.CaptureLayer;
            }
            // Measuring the instantiated renderers catches both broken import axes and a lost unit scale.
            if (!Measured.Contains(kind))
            { ValidatePlacedModel(kind,wrapper); Measured.Add(kind); }
            wrapper.transform.localPosition = position;
            wrapper.transform.localRotation = rotation;
            return wrapper;
        }

        public static void ValidatePlacedModel(string kind, GameObject wrapper)
        {
            bool fishProp = Array.IndexOf(FishModelNames, kind) >= 0;
            Manifest measuredManifest = fishProp ? fishManifest : manifest;
            if (measuredManifest == null)
            {
                TextAsset source = Resources.Load<TextAsset>(ResourceFolder + (fishProp ? "WarehouseFishProps3D" : "CombatWarehouse3D"));
                if (source == null) throw new InvalidOperationException("Missing measured warehouse manifest.");
                measuredManifest = JsonUtility.FromJson<Manifest>(source.text);
                if (fishProp) fishManifest = measuredManifest;
                else manifest = measuredManifest;
            }
            Model expected = Array.Find(measuredManifest.models,item => item.name == kind);
            if (expected == null) throw new InvalidOperationException("Warehouse manifest does not describe " + kind);
            Bounds actual = default; bool found = false;
            foreach (MeshRenderer renderer in wrapper.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled) continue;
                // The wrapper can be parented and placed anywhere. Measure all eight world-bounds
                // corners back in its local metre frame before comparing the generated contract.
                Bounds bounds = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = wrapper.transform.InverseTransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f,(corner & 2) == 0 ? -1f : 1f,(corner & 4) == 0 ? -1f : 1f)));
                    if (!found) { actual = new Bounds(point,Vector3.zero); found = true; }
                    else actual.Encapsulate(point);
                }
            }
            Vector3 min = new Vector3(expected.bounds_min[0],expected.bounds_min[1],expected.bounds_min[2]);
            Vector3 max = new Vector3(expected.bounds_max[0],expected.bounds_max[1],expected.bounds_max[2]);
            if (!found || Vector3.Distance(actual.min,min) > .03f || Vector3.Distance(actual.max,max) > .03f)
                throw new InvalidOperationException($"Warehouse {kind} imported bounds {actual.min}..{actual.max} disagree with {min}..{max}.");
        }

        public static Transform FindAnchor(GameObject model, string name)
        {
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            throw new InvalidOperationException("Warehouse model lacks anchor " + name);
        }

        public static string AtlasForModel(string kind)
        {
            if (Array.IndexOf(FishModelNames, kind) >= 0) return null;
            if (Array.IndexOf(ModelNames,kind) < 0) throw new ArgumentOutOfRangeException(nameof(kind));
            return "WarehouseMasterAtlas";
        }

        public static Material GetPropSurface(string role) => Surface("Auxiliary", role);

        public static int SurfaceSlotCount(string kind)
        {
            if (Array.IndexOf(ModelNames, kind) < 0) throw new ArgumentOutOfRangeException(nameof(kind));
            if (Array.IndexOf(FishModelNames, kind) >= 0) return 1;
            LoadSurfaceVariants();
            return SurfaceVariants.TryGetValue(kind, out Dictionary<int, Dictionary<string, Mesh>> slots) ? slots.Count : 1;
        }

        private static string SurfaceSourceName(string name)
        {
            int suffix = name.LastIndexOf('.');
            return suffix >= 0 && int.TryParse(name.Substring(suffix + 1), out _) ? name.Substring(0, suffix) : name;
        }

        private static void LoadSurfaceVariants()
        {
            if (variantsLoaded) return;
            TextAsset source = Resources.Load<TextAsset>(ResourceFolder + "WarehouseSurfaceVariants");
            GameObject library = Resources.Load<GameObject>(ResourceFolder + "WarehouseSurfaceVariants");
            SurfaceVariantManifest records = source == null ? null : JsonUtility.FromJson<SurfaceVariantManifest>(source.text);
            if (library == null || records?.variants == null)
                throw new InvalidOperationException("Warehouse requires its bounded authored surface variant library.");
            var meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            foreach (MeshFilter filter in library.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == null || !meshes.TryAdd(filter.name, filter.sharedMesh))
                    throw new InvalidOperationException("Warehouse surface library contains an empty or repeated mesh: " + filter.name);
            foreach (SurfaceVariant record in records.variants)
            {
                if (record == null || Array.IndexOf(ModelNames, record.kind) < 0 || record.slot < 0 || record.meshes == null)
                    throw new InvalidOperationException("Warehouse surface variant record is invalid.");
                if (!SurfaceVariants.TryGetValue(record.kind, out Dictionary<int, Dictionary<string, Mesh>> slots))
                {
                    slots = new Dictionary<int, Dictionary<string, Mesh>> { [0] = null };
                    SurfaceVariants.Add(record.kind, slots);
                }
                var replacements = new Dictionary<string, Mesh>(StringComparer.Ordinal);
                foreach (SurfaceMesh part in record.meshes)
                    if (part == null || !meshes.TryGetValue(part.node, out Mesh mesh) ||
                        !replacements.TryAdd(part.source, mesh))
                        throw new InvalidOperationException("Warehouse surface variant mesh is absent or repeated: " + record.kind);
                if (record.slot == 0 && slots[0] == null) slots[0] = replacements;
                else if (!slots.TryAdd(record.slot, replacements))
                    throw new InvalidOperationException("Warehouse surface variant slot is repeated: " + record.kind);
            }
            foreach (KeyValuePair<string, Dictionary<int, Dictionary<string, Mesh>>> pair in SurfaceVariants)
                for (int slot = 0; slot < pair.Value.Count; slot++)
                    if (!pair.Value.ContainsKey(slot))
                        throw new InvalidOperationException("Warehouse surface slots must be contiguous: " + pair.Key);
            foreach (AuxiliarySurface surface in records.auxiliary ?? Array.Empty<AuxiliarySurface>())
            {
                if (surface?.rect == null || surface.rect.Length != 4 ||
                    !AuxiliaryRects.TryAdd(surface.role, new Vector4(surface.rect[0], surface.rect[1], surface.rect[2], surface.rect[3])))
                    throw new InvalidOperationException("Warehouse auxiliary material patch is invalid or repeated.");
            }
            variantsLoaded = true;
        }

        public static void SetSignalLight(GameObject fixture, bool enabled)
        {
            foreach (MeshRenderer renderer in fixture.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name.EndsWith("__SignalRed", StringComparison.Ordinal))
                    renderer.sharedMaterial = Surface("EmergencyRoomLamp", enabled ? "SignalRed" : "SignalRedOff");
        }

        internal static Material Surface(string kind, string role)
        {
            bool auxiliary = kind == "Auxiliary" || Array.IndexOf(FishModelNames, kind) >= 0;
            string atlas = auxiliary ? null : AtlasForModel(kind);
            string family = role == "WearDecal" ? "Wear decals" : atlas ?? "Props";
            bool fanLens = kind == "FanHousing" && role == "Lamp";
            bool fishBulb = kind == "FishStationLamp" && role == "Lamp";
            string key = family + "/" + (fishBulb ? "FishBulb" : fanLens ? "FanLens" : role);
            if (Materials.TryGetValue(key,out Material cached) && cached != null) return cached;
            string texture = null; Color color;
            switch (role)
            {
                case "Concrete": color = new Color(.84f,.88f,.83f); break;
                case "ConcreteDark": color = new Color(.57f,.63f,.58f); break;
                case "Steel": color = new Color(.72f,.77f,.70f); break;
                case "WornSteel": color = new Color(.88f,.88f,.82f); break;
                case "Rust": color = new Color(.90f,.83f,.72f); break;
                case "Timber": color = new Color(.80f,.77f,.68f); break;
                case "Cardboard": color = new Color(.88f,.82f,.72f); break;
                case "Glass": color = new Color(.22f,.30f,.28f); break;
                case "Lamp": color = new Color(.38f,.37f,.31f); break;
                case "SignalRed": color = new Color(.90f,.045f,.02f); break;
                case "SignalRedOff": color = new Color(.12f,.018f,.014f); break;
                case "SpoiledFish": color = new Color(.38f,.42f,.29f); break;
                case "SpoiledFishFlesh": color = new Color(.43f,.34f,.25f); break;
                case "SpoiledFishDark": color = new Color(.075f,.085f,.055f); break;
                case "FishLabelPaper": color = Color.white; texture = "WarehouseFishLabels"; break;
                case "FishLabelInk": color = new Color(.065f,.085f,.070f); break;
                case "SafetyYellow": color = new Color(.82f,.62f,.12f); break;
                case "NumberPlate": color = new Color(.83f,.79f,.64f); break;
                case "LockSteel": color = new Color(.44f,.46f,.45f); break;
                case "Rubber": color = new Color(.70f,.73f,.69f); break;
                case "WearDecal": color = Color.white; texture = "WarehouseWearDecals"; break;
                default: throw new InvalidOperationException("Unknown warehouse surface " + role);
            }
            // Role properties remain shared within each atlas family. A copied
            // cargo or mechanism never allocates another instance material.
            if (atlas != null && role != "WearDecal")
            {
                texture = atlas;
                // The master painting already includes each role's authored tint.
                // Tinting again would square that darkness on the props.
                color = Color.white;
            }
            bool layered = role != "WearDecal" && role != "Lamp" && role != "Glass" && role != "NumberPlate" &&
                role != "SignalRed" && role != "SignalRedOff";
            if (role == "FishLabelPaper" || role == "FishLabelInk") layered = false;
            if (auxiliary && layered)
            {
                texture = "WarehouseMasterAtlas";
                color = Color.white;
            }
            Shader shader = Resources.Load<Shader>(layered ? "Shaders/WarehouseSurface" : "Shaders/Lit");
            if (shader == null) throw new InvalidOperationException("Warehouse requires its project surface shader.");
            Texture2D sheet = texture == null ? Texture2D.whiteTexture : Resources.Load<Texture2D>(ResourceFolder + "Textures/" + texture);
            if (sheet == null) throw new InvalidOperationException("Warehouse is missing its shared surface " + texture);
            var material = new Material(shader) { name = "Warehouse " + key.Replace('/', ' ') + " Shared", hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true };
            material.SetTexture("_BaseMap",sheet); material.SetColor("_BaseColor",color.linear);
            material.SetFloat("_Smoothness",role == "Glass" ? .35f : .12f);
            material.SetFloat("_Metallic",role == "Steel" || role == "WornSteel" || role == "LockSteel" ? .25f : 0f);
            if (fishBulb)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_BaseColor", Color.white);
                material.SetColor("_EmissionColor", Color.white);
            }
            if (fanLens)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor",new Color(.92f,.86f,.65f).linear);
            }
            if (role == "SignalRed")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor",new Color(.72f,.012f,.005f).linear);
            }
            if (layered)
            {
                ConfigureMaterialResponse(material, role);
                material.SetFloat("_WarehouseAnisotropy", role == "WornSteel" ? .38f :
                    role == "Steel" || role == "LockSteel" ? .22f : 0f);
                material.SetFloat("_WarehouseWetReflection", role == "Concrete" ? .65f :
                    role == "ConcreteDark" ? .4f : 0f);
                if (auxiliary)
                {
                    LoadSurfaceVariants();
                    if (!AuxiliaryRects.TryGetValue(role, out Vector4 rect))
                        throw new InvalidOperationException("Warehouse lacks its bounded auxiliary material patch: " + role);
                    material.SetVector("_WarehouseAuxRect", rect);
                    material.SetFloat("_WarehouseAuxiliary", 1f);
                }
            }
            if (role == "WearDecal")
            {
                // Authored sparse planes use a cutout, preserving opaque depth
                // and the existing light/shadow/ambient presentation.
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetFloat("_AlphaClip",1f);
                material.SetFloat("_Cutoff",.16f);
                material.SetFloat("_Smoothness",.02f);
                material.SetOverrideTag("RenderType","TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            Materials[key] = material;
            return material;
        }

        private static void ConfigureMaterialResponse(Material material, string role)
        {
            float normal;
            switch (role)
            {
                case "Concrete": case "ConcreteDark": normal = .24f; break;
                case "Steel": case "WornSteel": case "LockSteel": case "SafetyYellow":
                    normal = .18f; break;
                case "Rust": normal = .26f; break;
                case "Timber": normal = .28f; break;
                case "Cardboard": normal = .12f; break;
                case "Rubber": normal = .09f; break;
                default: normal = .12f; break;
            }
            Texture2D response = Resources.Load<Texture2D>(ResourceFolder + "Textures/WarehouseMaterialResponse");
            if (response == null) throw new InvalidOperationException("Warehouse is missing its shared material response.");
            material.SetTexture("_WarehouseMaterialResponse", response);
            material.SetFloat("_WarehouseResponseStrength", .9f);
            material.SetFloat("_WarehouseNormalStrength", normal);
        }

        private static bool IsReflectionArchitecture(string kind)
        {
            switch (kind)
            {
                case "Shell": case "Platform": case "Stair": case "Rail":
                case "Rack": case "Cargo": case "Pipes": case "PipeStraight":
                case "PipeElbow": case "PipeEnd": case "RoomShell": case "FanHousing":
                case "LiftFrame": case "PusherFrame": case "TechnicalProps": case "BreakProps":
                case "Lamp": case "EmergencyLamp": case "EmergencyRoomLamp": case "FishStationLamp":
                    return true;
                default: return false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            foreach (Material material in Materials.Values) if (material != null) UnityEngine.Object.Destroy(material);
            Materials.Clear(); Templates.Clear(); Measured.Clear(); SurfaceVariants.Clear(); AuxiliaryRects.Clear();
            manifest = null; fishManifest = null; variantsLoaded = false;
        }
    }
}
