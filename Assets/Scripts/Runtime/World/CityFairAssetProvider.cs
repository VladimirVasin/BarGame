using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Passive fixed-metre fair kit. Placement always owns a unit wrapper;
    /// the FBX child's imported axis and unit factors remain intact.</summary>
    public static class CityFairAssetProvider
    {
        public const string ResourceFolder = "City/Fair/";
        public static readonly string[] ModelNames =
        {
            "Stall", "Bread", "Fruit", "Pottery", "WoodenToys", "Organ", "Bell",
            "Clutter", "Garland", "Bench"
        };
        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        public static GameObject Create(string kind, Transform parent)
        {
            if (Array.IndexOf(ModelNames, kind) < 0)
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown fair model.");
            if (!Templates.TryGetValue(kind, out GameObject template) || template == null)
            {
                template = Resources.Load<GameObject>(ResourceFolder + kind);
                if (template == null) throw new InvalidOperationException("Missing fair model: " + kind);
                Templates[kind] = template;
            }
            var wrapper = new GameObject(kind);
            wrapper.transform.SetParent(parent, false);
            GameObject model = UnityEngine.Object.Instantiate(template, wrapper.transform, false);
            NormalizeDrivenParts(wrapper);
            foreach (MeshRenderer renderer in wrapper.GetComponentsInChildren<MeshRenderer>(true))
            {
                int separator = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                if (separator < 0) throw new InvalidOperationException("Fair mesh lacks a surface role: " + renderer.name);
                string role = renderer.name.Substring(separator + 2);
                renderer.sharedMaterial = GetSurfaceMaterial(role);
                renderer.shadowCastingMode = role == "Bulb" ? ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            return wrapper;
        }

        private static void NormalizeDrivenParts(GameObject wrapper)
        {
            // Preserve the final imported world matrices and FBX unit factors.
            // Only each driven empty needs a canonical metre basis; its mesh and
            // grip children keep their exact world shapes and contact positions.
            foreach (Transform part in wrapper.GetComponentsInChildren<Transform>(true))
            {
                bool driven = part.name == "CrankPivot" || part.name == "BellSwingPivot" ||
                    part.name == "RopePivot" || part.name == "RopeSpanPivot" || part.name == "Wire" ||
                    part.name.StartsWith("Fixture_", StringComparison.Ordinal) ||
                    part.name.StartsWith("Mount_", StringComparison.Ordinal);
                if (!driven || part.GetComponent<MeshFilter>() != null) continue;
                var children = new Transform[part.childCount];
                for (int i = 0; i < children.Length; i++) children[i] = part.GetChild(i);
                foreach (Transform child in children) child.SetParent(wrapper.transform, true);
                part.SetParent(wrapper.transform, true);
                part.localRotation = Quaternion.identity;
                part.localScale = Vector3.one;
                foreach (Transform child in children) child.SetParent(part, true);
            }
        }

        /// <summary>Places the unit wrapper in world space; never rewrites imported transforms.</summary>
        public static GameObject Instantiate(string kind, Transform parent, Vector3 position, Quaternion rotation)
        {
            GameObject model = Create(kind, parent);
            model.transform.SetPositionAndRotation(position, rotation);
            return model;
        }

        /// <summary>Scales only the authored cable along its span. Bulbs, sockets and
        /// facade mounting plates retain their measured size and 0.65-metre sag.</summary>
        public static GameObject CreateGarland(Transform parent, Vector3 start, Vector3 end)
        {
            Vector3 delta = end - start;
            if (delta.magnitude < 1f) throw new ArgumentException("A fair garland needs a span of at least one metre.");
            GameObject model = Create("Garland", parent);
            float scale = delta.magnitude / 10f;
            Transform wire = FindPart(model, "Wire");
            wire.localScale = Vector3.Scale(wire.localScale, new Vector3(scale, 1f, 1f));
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                if (!part.name.StartsWith("Fixture_", StringComparison.Ordinal) &&
                    !part.name.StartsWith("Mount_", StringComparison.Ordinal) && part.name != "SpanEnd") continue;
                // Only semantic empty roots, not the same-prefix renderer children.
                if (part.GetComponent<MeshFilter>() != null) continue;
                Vector3 local = model.transform.InverseTransformPoint(part.position);
                local.x *= scale;
                part.position = model.transform.TransformPoint(local);
                // Mount plates face the opposite end walls; the cable leaves
                // each plate into the gap rather than along its facade.
                if (part.name.StartsWith("Mount_", StringComparison.Ordinal))
                    part.localRotation = Quaternion.Euler(0f, part.name == "Mount_00" ? 90f : -90f, 0f);
            }
            model.transform.SetPositionAndRotation(start, Quaternion.FromToRotation(Vector3.right, delta.normalized));
            return model;
        }

        public static Transform FindPart(GameObject model, string name)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
                if (string.Equals(part.name, name, StringComparison.Ordinal)) return part;
            throw new InvalidOperationException($"Fair model '{model.name}' has no '{name}' part.");
        }

        /// <summary>One shared material per role; existing city sheets keep metre UVs
        /// and ordinary wood/fabric/metal wear consistent with the surrounding town.</summary>
        public static Material GetSurfaceMaterial(string role)
        {
            if (Materials.TryGetValue(role, out Material material) && material != null) return material;
            Color color;
            string texture = null;
            switch (role)
            {
                case "Timber": color = new Color(.80f,.67f,.51f); texture = "PortTimberAlbedo"; break;
                case "TimberLight": color = new Color(.96f,.85f,.66f); texture = "PortTimberAlbedo"; break;
                case "TimberDark": color = new Color(.52f,.46f,.39f); texture = "PortTimberAlbedo"; break;
                case "FabricPlum": color = new Color(.67f,.46f,.59f); texture = "PortFabricAlbedo"; break;
                case "FabricCream": color = new Color(.97f,.91f,.76f); texture = "PortFabricAlbedo"; break;
                case "Steel": color = new Color(.47f,.51f,.49f); texture = "PortPaintedSteelAlbedo"; break;
                case "Brass": color = new Color(.56f,.43f,.22f); break;
                case "Rubber": color = new Color(.115f,.12f,.115f); break;
                case "Bread": color = new Color(.67f,.40f,.15f); break;
                case "Crust": color = new Color(.40f,.215f,.075f); break;
                case "Flour": color = new Color(.77f,.64f,.40f); break;
                case "Apple": color = new Color(.53f,.205f,.13f); break;
                case "Pear": color = new Color(.58f,.58f,.245f); break;
                case "Leaf": color = new Color(.27f,.33f,.17f); break;
                case "Clay": color = new Color(.57f,.31f,.195f); break;
                case "Glaze": color = new Color(.28f,.435f,.40f); break;
                case "Rope": color = new Color(.57f,.50f,.35f); break;
                case "Bulb": color = new Color(.95f,.70f,.36f); break;
                default: throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown fair surface.");
            }
            Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
            if (shader == null) throw new InvalidOperationException("Fair requires the shared PS1 Lit shader.");
            Texture2D sheet = texture == null ? Texture2D.whiteTexture :
                Resources.Load<Texture2D>("City/Port/Textures/" + texture);
            if (sheet == null) throw new InvalidOperationException("Fair shared city texture is missing: " + texture);
            material = new Material(shader) { name = "Fair " + role + " Shared", hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap", sheet);
            material.SetColor("_BaseColor", color.linear);
            material.SetFloat("_Smoothness", role == "Glaze" ? .35f : role == "Brass" ? .30f : .10f);
            material.SetFloat("_Metallic", role == "Brass" ? .38f : role == "Steel" ? .15f : 0f);
            if (role == "Bulb")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color.linear * 2.4f);
            }
            Materials[role] = material;
            return material;
        }
    }
}
