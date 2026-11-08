using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Places the Blender yard kit once and conforms its authored gravel to the existing ground.</summary>
    public sealed class CityCobblerYardWorld : MonoBehaviour
    {
        private const string ResourcePath = "City/Cobbler/CobblerYard";
        private static GameObject template;
        private static Material material;
        private static Manifest geometry;
        private readonly List<Mesh> fittedMeshes = new(2);

        [Serializable]
        internal sealed class PartData
        {
            public string name;
            public CityCobblerAssetProvider.MetricBox[] boxes;
        }
        [Serializable]
        internal sealed class Manifest
        {
            public PartData[] parts;
            internal PartData Part(string name)
            {
                foreach (var part in parts) if (part.name == name) return part;
                throw new InvalidOperationException("Missing cobbler yard part " + name);
            }
        }
        internal static Manifest Geometry => geometry ??= JsonUtility.FromJson<Manifest>(
            Resources.Load<TextAsset>(ResourcePath).text);

        internal static void Build(Transform parent, CityCobblerSitePlan site, bool interactive)
        {
            if (!template) template = Resources.Load<GameObject>(ResourcePath);
            if (!material) material = Resources.Load<Material>("City/Cobbler/CobblerSurface");
            if (!template || !material) throw new InvalidOperationException("Missing cobbler yard model or shared paint.");
            var root = new GameObject("Cobbler fitting yard").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var yard = root.gameObject.AddComponent<CityCobblerYardWorld>();
            var plan = site.Yard;
            foreach (var item in plan.Placements)
            {
                Transform part = Clone(item.Part, root, interactive);
                part.SetPositionAndRotation(item.Position, item.Rotation);
            }
            // Geometry is authored in Blender. Only its existing vertices are
            // adjusted here, retaining winding, UVs and the ground's actual shape.
            yard.Gravel(root, site, plan.PathStart, plan.PathEnd, .008f);
            yard.Gravel(root, site, Vector3.Lerp(plan.PathStart, plan.PathEnd, .72f), plan.BenchPathEnd, .016f);
            var cable = Clone("FeedCable", root, false);
            Vector3 span = plan.CableEnd - plan.CableStart;
            cable.SetPositionAndRotation(plan.CableStart, Quaternion.LookRotation(span, Vector3.up));
            Vector3 scale = cable.localScale;
            scale.z *= span.magnitude / 3f;
            cable.localScale = scale;
        }

        private static Transform Clone(string partName, Transform parent, bool interactive)
        {
            // Clone the whole FBX to preserve import axes and unit scale, then
            // retain only the requested independent metre-rooted part.
            var placement = new GameObject(partName).transform;
            placement.SetParent(parent, false);
            var shell = Instantiate(template, placement, false).transform;
            var selected = CityCobblerAssetProvider.FindPart(shell, partName);
            foreach (var part in Geometry.parts)
            {
                if (part.name == partName) continue;
                var unused = CityCobblerAssetProvider.FindPart(shell, part.name);
                unused.gameObject.SetActive(false);
                Destroy(unused.gameObject);
            }
            foreach (var renderer in selected.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name.StartsWith("COL_", StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                    if (!interactive) continue;
                    Bounds box = renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
                    var collider = renderer.gameObject.AddComponent<BoxCollider>();
                    collider.center = box.center; collider.size = box.size;
                }
                else renderer.sharedMaterial = material;
            }
            return placement;
        }

        private void Gravel(Transform root, CityCobblerSitePlan site, Vector3 from, Vector3 to, float lift)
        {
            var strip = Clone("GravelApproach", root, false);
            Vector3 forward = Vector3.ProjectOnPlane(to - from, Vector3.up);
            strip.SetPositionAndRotation((from + to) * .5f, Quaternion.LookRotation(forward));
            // FBX local axes are importer-owned. Fit through world coordinates
            // before changing the authored point's height, never assume local Y.
            var filter = strip.GetComponentInChildren<MeshFilter>();
            var mesh = Instantiate(filter.sharedMesh);
            fittedMeshes.Add(mesh);
            Vector3[] vertices = mesh.vertices;
            var ground = CityHillsideGroundPlan.ForLayout(site.Layout);
            var road = CityHillsideStreetPlan.ForLayout(site.Layout);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 authored = filter.transform.TransformPoint(vertices[i]);
                float along = Vector3.Dot(authored - strip.position, strip.forward);
                Vector3 point = authored + strip.forward * (along * (forward.magnitude - 1f));
                var xz = new Vector2(point.x, point.z);
                if (!ground.TrySampleTop(xz, out float top))
                    throw new InvalidOperationException("Missing gravel support at " + xz);
                foreach (var street in road.Routes)
                    if (street.TrySampleTop(xz, out float paved, out _)) top = Mathf.Max(top, paved);
                point.y = top + lift;
                vertices[i] = filter.transform.InverseTransformPoint(point);
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds(); mesh.RecalculateNormals();
            filter.sharedMesh = mesh;
        }

        private void OnDestroy()
        {
            foreach (var mesh in fittedMeshes) if (mesh) Destroy(mesh);
        }
    }
}
