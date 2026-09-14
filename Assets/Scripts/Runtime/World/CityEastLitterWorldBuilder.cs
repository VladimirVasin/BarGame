using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>Rigid shared Blender meshes settle on the existing ground, without per-instance mesh copies.</summary>
    public static class CityEastLitterWorldBuilder
    {
        public const string RootName = "Eastern Roadside Litter";
        internal delegate bool SampleGround(Vector2 point, out float height);

        internal static Transform Build(Transform parent, CityEastExitPlan exit, SampleGround sample)
        {
            CityEastLitterPlan plan = CityEastLitterPlan.Create(exit);
            GameObject asset = Resources.Load<GameObject>(CityLitterCatalog.ResourcePath);
            if (asset == null) throw new InvalidOperationException("Missing authored litter model: " + CityLitterCatalog.ResourcePath);
            var sources = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform child in asset.GetComponentsInChildren<Transform>(true))
                if (!sources.ContainsKey(child.name)) sources.Add(child.name, child);
            var templates = new Dictionary<string, Template>(StringComparer.Ordinal);
            foreach (CityLitterItem item in CityLitterCatalog.Load().Items)
            {
                if (!sources.TryGetValue(item.Name, out Transform source))
                    throw new InvalidOperationException("Missing litter assembly: " + item.Name);
                templates.Add(item.Name, new Template(source, item));
            }
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            var appearances = new Dictionary<string, MaterialPropertyBlock>(StringComparer.Ordinal);
            int triangles = 0;
            foreach (CityEastLitterPart part in plan.Parts)
            {
                Template template = templates[part.Item.Name];
                // Use every actual vertex to find the first supporting contact.
                // Tilting a rigid bottle is valid; bending it like grass is not.
                float lift = float.NegativeInfinity;
                foreach (Vector3 vertex in template.Vertices)
                {
                    Vector3 world = part.Position + part.Rotation * (vertex * part.Scale);
                    if (!sample(new Vector2(world.x, world.z), out float ground))
                        throw new InvalidOperationException("Litter left its actual ground support: " + part.Id);
                    lift = Mathf.Max(lift, ground - world.y + .002f);
                }
                Transform placement = new GameObject(part.Id).transform;
                placement.SetParent(root, false);
                placement.SetPositionAndRotation(part.Position + Vector3.up * lift, part.Rotation);
                placement.localScale = Vector3.one * part.Scale;
                Transform clone = Object.Instantiate(template.Root, placement, false);
                clone.localPosition = Vector3.zero;
                clone.localRotation = template.Root.rotation;
                clone.localScale = template.Root.lossyScale;
                foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
                {
                    string role = renderer.name.Substring(renderer.name.LastIndexOf('_') + 1);
                    if (!appearances.TryGetValue(role, out MaterialPropertyBlock properties))
                    {
                        Color tint = Tint(role);
                        properties = new MaterialPropertyBlock();
                        properties.SetColor("_BaseColor", tint); properties.SetColor("_Color", tint);
                        properties.SetFloat("_Smoothness", role.StartsWith("Glass", StringComparison.Ordinal) ? .30f : .10f);
                        properties.SetFloat("_Metallic", role == "Steel" ? .18f : 0f);
                        appearances.Add(role, properties);
                    }
                    renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                    renderer.SetPropertyBlock(properties);
                    renderer.shadowCastingMode = part.Item.Solid ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                }
                if (part.Item.Solid)
                {
                    BoxCollider collider = placement.gameObject.AddComponent<BoxCollider>();
                    collider.center = part.Item.Bounds.center;
                    collider.size = part.Item.Bounds.size;
                }
                triangles += part.Item.TriangleCount;
            }
            GameLog.Debug("city", "east_litter_built", GameLog.Field("parts", plan.Parts.Count),
                GameLog.Field("variants", templates.Count), GameLog.Field("triangles", triangles));
            return root;
        }

        private sealed class Template
        {
            internal Transform Root { get; }
            internal Vector3[] Vertices { get; }
            internal Template(Transform root, CityLitterItem item)
            {
                Root = root;
                var vertices = new List<Vector3>();
                Bounds measured = default;
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null || !mesh.isReadable)
                        throw new InvalidOperationException("Litter import must retain readable shared meshes: " + item.Name);
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point = filter.transform.TransformPoint(vertex) - root.position;
                        if (vertices.Count == 0) measured = new Bounds(point, Vector3.zero);
                        else measured.Encapsulate(point);
                        vertices.Add(point);
                    }
                }
                if (vertices.Count == 0 || (measured.min - item.Bounds.min).sqrMagnitude > .0001f ||
                    (measured.max - item.Bounds.max).sqrMagnitude > .0001f)
                    throw new InvalidOperationException("Imported litter changed its measured metre bounds: " + item.Name + " " + measured);
                Vertices = vertices.ToArray();
            }
        }

        private static Color Tint(string role)
        {
            switch (role)
            {
                case "GlassGreen": return new Color(.16f, .24f, .16f);
                case "GlassBrown": return new Color(.25f, .16f, .08f);
                case "GlassClear": return new Color(.36f, .42f, .38f);
                case "Steel": return new Color(.35f, .37f, .33f);
                case "Rust": return new Color(.30f, .17f, .10f);
                case "PaintGreen": return new Color(.19f, .28f, .23f);
                case "PaintBlue": return new Color(.21f, .29f, .33f);
                case "Rubber": return new Color(.065f, .073f, .065f);
                case "Plastic": return new Color(.40f, .42f, .32f);
                case "Paper": return new Color(.43f, .37f, .26f);
                case "Timber": return new Color(.29f, .25f, .16f);
                case "Brick": return new Color(.36f, .24f, .17f);
                case "Dark": return new Color(.09f, .10f, .08f);
                default: throw new InvalidOperationException("Unknown shared litter surface: " + role);
            }
        }
    }
}
