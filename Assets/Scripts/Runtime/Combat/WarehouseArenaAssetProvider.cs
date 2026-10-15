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
        public static readonly string[] TextureNames = { "WarehouseConcrete", "WarehousePaint", "WarehouseSteel",
            "WarehouseRust", "WarehouseTimber", "WarehouseCardboard", "WarehouseRubber",
            "WarehouseMechanismsAtlas", "WarehouseFurnishingsAtlas", "WarehouseCargoAtlas",
            "WarehouseMacroMask", "WarehouseWearDecals", "WarehouseFishLabels" };
        public static readonly string[] AtlasTextureNames = { "WarehouseMechanismsAtlas", "WarehouseFurnishingsAtlas",
            "WarehouseCargoAtlas" };
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();
        private static readonly HashSet<string> Measured = new HashSet<string>();
        private static Manifest manifest;
        private static Manifest fishManifest;

        [Serializable] private sealed class Manifest { public Model[] models; }
        [Serializable] private sealed class Model { public string name; public float[] bounds_min, bounds_max; }

        public static GameObject Create(string kind, Transform parent, Vector3 position, Quaternion rotation)
        {
            if (Array.IndexOf(ModelNames,kind) < 0) throw new ArgumentOutOfRangeException(nameof(kind));
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
                int separator = renderer.name.LastIndexOf("__",StringComparison.Ordinal);
                if (separator < 0) throw new InvalidOperationException("Warehouse mesh lacks a surface role: " + renderer.name);
                string role = renderer.name.Substring(separator + 2);
                renderer.sharedMaterial = Surface(kind,role);
                renderer.shadowCastingMode = role == "Lamp" || role == "SignalRed" || role == "SignalRedOff" || role == "Glass" || role == "WearDecal" ?
                    ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
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
            switch (kind)
            {
                case "Shell": case "RoomShell": case "Platform": case "Stair": case "Rail": return null;
                case "MainDoor": case "LockerProps": case "BreakProps": case "TechnicalProps": case "Lamp":
                case "EmergencyLamp":
                case "RoomSign1": case "RoomSign2": case "RoomSign3": case "RoomSign4": case "RoomSign5":
                case "RoomSign6": case "RoomSign7": case "RoomSign8": case "RoomSign9":
                    return "WarehouseFurnishingsAtlas";
                case "Rack": case "Cargo": return "WarehouseCargoAtlas";
                default:
                    if (Array.IndexOf(ModelNames,kind) < 0) throw new ArgumentOutOfRangeException(nameof(kind));
                    return "WarehouseMechanismsAtlas";
            }
        }

        public static Material GetPropSurface(string role) => Surface("Shell", role);

        public static void SetSignalLight(GameObject fixture, bool enabled)
        {
            foreach (MeshRenderer renderer in fixture.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name.EndsWith("__SignalRed", StringComparison.Ordinal))
                    renderer.sharedMaterial = Surface("EmergencyRoomLamp", enabled ? "SignalRed" : "SignalRedOff");
        }

        internal static Material Surface(string kind, string role)
        {
            string atlas = AtlasForModel(kind);
            string family = role == "WearDecal" ? "Wear decals" : atlas ?? "Structure";
            bool fanLens = kind == "FanHousing" && role == "Lamp";
            bool fishBulb = kind == "FishStationLamp" && role == "Lamp";
            string key = family + "/" + (fishBulb ? "FishBulb" : fanLens ? "FanLens" : role);
            if (Materials.TryGetValue(key,out Material cached) && cached != null) return cached;
            string texture = null; Color color;
            switch (role)
            {
                case "Concrete": color = new Color(.84f,.88f,.83f); texture = "WarehouseConcrete"; break;
                case "ConcreteDark": color = new Color(.57f,.63f,.58f); texture = "WarehouseConcrete"; break;
                case "Steel": color = new Color(.72f,.77f,.70f); texture = "WarehousePaint"; break;
                case "WornSteel": color = new Color(.88f,.88f,.82f); texture = "WarehouseSteel"; break;
                case "Rust": color = new Color(.90f,.83f,.72f); texture = "WarehouseRust"; break;
                case "Timber": color = new Color(.80f,.77f,.68f); texture = "WarehouseTimber"; break;
                case "Cardboard": color = new Color(.88f,.82f,.72f); texture = "WarehouseCardboard"; break;
                case "Glass": color = new Color(.22f,.30f,.28f); break;
                case "Lamp": color = new Color(.38f,.37f,.31f); break;
                case "SignalRed": color = new Color(.90f,.045f,.02f); break;
                case "SignalRedOff": color = new Color(.12f,.018f,.014f); break;
                case "SpoiledFish": color = new Color(.38f,.42f,.29f); texture = "WarehouseConcrete"; break;
                case "SpoiledFishFlesh": color = new Color(.43f,.34f,.25f); texture = "WarehouseConcrete"; break;
                case "SpoiledFishDark": color = new Color(.075f,.085f,.055f); texture = "WarehouseRubber"; break;
                case "FishLabelPaper": color = Color.white; texture = "WarehouseFishLabels"; break;
                case "FishLabelInk": color = new Color(.065f,.085f,.070f); break;
                case "SafetyYellow": color = new Color(.82f,.62f,.12f); break;
                case "NumberPlate": color = new Color(.83f,.79f,.64f); break;
                case "LockSteel": color = new Color(.44f,.46f,.45f); break;
                case "Rubber": color = new Color(.70f,.73f,.69f); texture = "WarehouseRubber"; break;
                case "WearDecal": color = Color.white; texture = "WarehouseWearDecals"; break;
                default: throw new InvalidOperationException("Unknown warehouse surface " + role);
            }
            // Role properties remain shared within each atlas family. A copied
            // cargo or mechanism never allocates another instance material.
            if (atlas != null && role != "WearDecal")
            {
                texture = atlas;
                // The EMIT bake already includes each role's authored tint.
                // Tinting again would square that darkness on the props.
                color = Color.white;
            }
            bool layered = atlas == null && role != "WearDecal" && role != "Lamp" && role != "Glass" &&
                role != "SignalRed" && role != "SignalRedOff";
            if (role == "FishLabelPaper") layered = false;
            Shader shader = Resources.Load<Shader>(layered ? "Shaders/WarehouseSurface" : "Shaders/Ps1Lit");
            if (shader == null) throw new InvalidOperationException("Warehouse requires its PS1 surface shader.");
            Texture2D sheet = texture == null ? Texture2D.whiteTexture : Resources.Load<Texture2D>(ResourceFolder + "Textures/" + texture);
            if (sheet == null) throw new InvalidOperationException("Warehouse is missing its shared surface " + texture);
            var material = new Material(shader) { name = "Warehouse " + family + " " + role + " Shared", hideFlags = HideFlags.HideAndDontSave,
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
                Texture2D mask = Resources.Load<Texture2D>(ResourceFolder + "Textures/WarehouseMacroMask");
                if (mask == null) throw new InvalidOperationException("Warehouse requires its shared macro variation mask.");
                material.SetTexture("_WarehouseMacroMask",mask);
                material.SetFloat("_WarehouseMacroStrength",.72f);
                material.SetFloat("_WarehouseRustStrength",role == "Steel" || role == "WornSteel" || role == "Rust" ? .46f : .12f);
                material.SetFloat("_WarehouseTriplanarStrength",0f);
            }
            if (role == "WearDecal")
            {
                // Authored sparse planes use a cutout, preserving opaque depth
                // and the existing PS1 light/shadow/ambient presentation.
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            foreach (Material material in Materials.Values) if (material != null) UnityEngine.Object.Destroy(material);
            Materials.Clear(); Templates.Clear(); Measured.Clear(); manifest = null; fishManifest = null;
        }
    }
}
