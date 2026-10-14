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
            "PipeStraight", "PipeElbow", "PipeEnd" };
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();
        private static readonly HashSet<string> Measured = new HashSet<string>();
        private static Manifest manifest;

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
                renderer.sharedMaterial = Surface(role);
                renderer.shadowCastingMode = role == "Lamp" || role == "Glass" ? ShadowCastingMode.Off : ShadowCastingMode.On;
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
            if (manifest == null)
            {
                TextAsset source = Resources.Load<TextAsset>(ResourceFolder + "CombatWarehouse3D");
                if (source == null) throw new InvalidOperationException("Missing measured warehouse manifest.");
                manifest = JsonUtility.FromJson<Manifest>(source.text);
            }
            Model expected = Array.Find(manifest.models,item => item.name == kind);
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

        private static Material Surface(string role)
        {
            if (Materials.TryGetValue(role,out Material cached) && cached != null) return cached;
            string texture = null; Color color;
            switch (role)
            {
                case "Concrete": color = new Color(.68f,.70f,.66f); texture = "PortConcreteAlbedo"; break;
                case "ConcreteDark": color = new Color(.48f,.51f,.47f); texture = "PortConcreteWallAlbedo"; break;
                case "Steel": color = new Color(.38f,.47f,.43f); texture = "PortPaintedSteelAlbedo"; break;
                case "WornSteel": color = new Color(.61f,.64f,.57f); texture = "PortPaintedSteelAlbedo"; break;
                case "Rust": color = new Color(.45f,.31f,.22f); texture = "PortPaintedSteelAlbedo"; break;
                case "Timber": color = new Color(.77f,.65f,.48f); texture = "PortTimberAlbedo"; break;
                case "Cardboard": color = new Color(.55f,.45f,.30f); texture = "PortTimberAlbedo"; break;
                case "Glass": color = new Color(.22f,.30f,.28f); break;
                case "Lamp": color = new Color(.38f,.37f,.31f); break;
                case "Rubber": color = new Color(.12f,.15f,.13f); texture = "PortRubberAlbedo"; break;
                default: throw new InvalidOperationException("Unknown warehouse surface " + role);
            }
            Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
            if (shader == null) throw new InvalidOperationException("Warehouse requires the shared PS1 shader.");
            Texture2D sheet = texture == null ? Texture2D.whiteTexture : Resources.Load<Texture2D>("City/Port/Textures/" + texture);
            if (sheet == null) throw new InvalidOperationException("Warehouse is missing a shared port surface " + texture);
            var material = new Material(shader) { name = "Warehouse " + role + " Shared", hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap",sheet); material.SetColor("_BaseColor",color.linear);
            material.SetFloat("_Smoothness",role == "Glass" ? .35f : .12f);
            material.SetFloat("_Metallic",role == "Steel" || role == "WornSteel" ? .25f : 0f);
            Materials[role] = material;
            return material;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            foreach (Material material in Materials.Values) if (material != null) UnityEngine.Object.Destroy(material);
            Materials.Clear(); Templates.Clear(); Measured.Clear(); manifest = null;
        }
    }
}
