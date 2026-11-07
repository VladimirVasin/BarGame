using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>Imported domestic yard pieces placed on the surveyed earth.</summary>
    internal static class CityPigeonCourtyardWorld
    {
        private const string Folder = "City/PigeonCourtyard/";
        internal const float LampNightIntensity = 6f;
        internal const float LampRange = 8f;

        internal static Transform Build(Transform parent, CityLayout layout, bool live, out CityPigeonCourtyardGate gate)
        {
            var plan = CityPigeonLoftPlan.ForLayout(layout).Courtyard;
            var root = new GameObject("Pigeon loft courtyard").transform;
            root.SetParent(parent, false);
            var prefab = Resources.Load<GameObject>(Folder + "PigeonCourtyard");
            if (!prefab) throw new InvalidOperationException("Missing imported pigeon courtyard model.");
            var templates = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform part in prefab.GetComponentsInChildren<Transform>(true)) templates[part.name] = part;
            var material = Resources.Load<Material>("City/PigeonLoft/PigeonLoftSurface");
            if (!material) throw new InvalidOperationException("Missing shared pigeon loft surface.");

            var fenceParts = new List<Transform>();
            for (int index = 0; index < plan.FencePosts.Count; index++)
            {
                Vector3 foot = plan.FencePosts[index] - Vector3.up * .04f;
                fenceParts.Add(Place("FencePost", foot, Quaternion.identity, Vector3.one));
                if (index == 0) continue;
                Vector3 start = plan.FencePosts[index - 1] - Vector3.up * .02f;
                Vector3 delta = foot - start;
                fenceParts.Add(Place(index == 7 ? "FencePanelRepair" : "FencePanel", start, AlongGround(delta),
                    new Vector3(delta.magnitude, 1f, 1f)));
            }
            CombinePieces(fenceParts, "Courtyard grille", live);
            Vector3 hingeFoot = plan.FencePosts[0];
            Vector3 closedSpan = plan.FencePosts[plan.FencePosts.Count - 1] - hingeFoot;
            Quaternion closedGate = Quaternion.LookRotation(Vector3.Cross(closedSpan, Vector3.up), Vector3.up);
            var hinge = new GameObject("Pigeon courtyard gate").transform;
            hinge.SetParent(root, false);
            hinge.localPosition = hingeFoot;
            hinge.localRotation = closedGate;
            Transform gateModel = Place("Gate", hingeFoot, closedGate, new Vector3(1.55f / 1.45f, 1f, 1f));
            gateModel.SetParent(hinge, true);
            gate = hinge.gameObject.AddComponent<CityPigeonCourtyardGate>();
            gate.Initialize(hinge, plan, live, CityHillsideGroundPlan.ForLayout(layout));

            Transform bench = Place("Workbench", plan.Workbench, Quaternion.Euler(0f, 180f, 0f), Vector3.one, live);
            Vector3 lampStand = bench.TransformPoint(new Vector3(-.64f, .8525f, .18f));
            Transform lamp = Place("HandLamp", lampStand, Quaternion.Euler(0f, 15f, 0f), Vector3.one);
            lamp.name = "Pigeon Keeper Hand Lamp";
            foreach (var renderer in lamp.GetComponentsInChildren<MeshRenderer>())
                if (renderer.name == "HandLampGlass")
                {
                    renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
                    Color glow = CityHandLampWorldBuilder.LampColor * 1.1f;
                    glow.a = 1f;
                    RuntimePrimitiveFactory.SetColor(renderer, glow);
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            CityHandLampWorldBuilder.AddLight(lamp, LampNightIntensity / CityHandLampWorldBuilder.NightIntensity, LampRange);
            Place("GrainStore", plan.GrainStore, Quaternion.Euler(0f, -90f, 0f), Vector3.one, live);
            Transform bath = Place("Bath", plan.Bath, Quaternion.identity, Vector3.one, live);
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            var gravelParts = new List<Transform>();
            for (int segment = 1; segment < plan.WalkPath.Count; segment++)
            {
                Vector3 a = plan.WalkPath[segment - 1], b = plan.WalkPath[segment];
                int patches = Mathf.CeilToInt(Vector3.Distance(a, b) / .8f);
                Quaternion rotation = AlongGround(Vector3.ProjectOnPlane(b - a, Vector3.up));
                for (int patch = 0; patch < patches; patch++)
                {
                    Vector3 point = Vector3.Lerp(a, b, (patch + .5f) / patches);
                    if (!ground.TrySampleTop(new Vector2(point.x, point.z), out float top)) continue;
                    gravelParts.Add(Place("GravelPatch", new Vector3(point.x, top + .009f, point.z), rotation,
                        new Vector3(Vector3.Distance(a, b) / patches, 1f, 1.45f)));
                }
            }
            CombinePieces(gravelParts, "Worn courtyard path", false, ground);
            return bath;

            Transform Place(string name, Vector3 position, Quaternion rotation, Vector3 scale, bool collision = false)
            {
                if (!templates.TryGetValue(name, out Transform template))
                    throw new InvalidOperationException("Missing pigeon courtyard piece " + name);
                var host = new GameObject(name).transform;
                host.SetParent(root, false);
                host.localPosition = position;
                host.localRotation = rotation;
                host.localScale = scale;
                Transform instance = Object.Instantiate(template.gameObject, host, false).transform;
                // The FBX root owns the centimetre factor and axis conversion.
                // Retain its full metre basis when separating a template child.
                instance.localPosition = Vector3.zero;
                instance.localRotation = template.rotation;
                instance.localScale = template.lossyScale;
                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>())
                {
                    renderer.sharedMaterial = material;
                    if (collision && renderer.name != "BathWater")
                        renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                }
                return host;
            }

            void CombinePieces(List<Transform> parts, string name, bool collision, CityHillsideGroundPlan conform = null)
            {
                var instances = new List<CombineInstance>();
                var patchPlanes = conform != null ? new List<float>() : null;
                foreach (Transform part in parts)
                    foreach (var filter in part.GetComponentsInChildren<MeshFilter>())
                    {
                        instances.Add(new CombineInstance { mesh = filter.sharedMesh,
                            transform = root.worldToLocalMatrix * filter.transform.localToWorldMatrix });
                        if (patchPlanes != null)
                            for (int vertex = 0; vertex < filter.sharedMesh.vertexCount; vertex++)
                                patchPlanes.Add(part.localPosition.y);
                    }
                var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(instances.ToArray(), true, true);
                if (conform != null)
                {
                    Vector3[] vertices = mesh.vertices;
                    // Each imported patch is shallow. Drape its silhouette onto
                    // the measured triangles so no flat edge hovers over a grade.
                    for (int index = 0; index < vertices.Length; index++)
                        if (conform.TrySampleTop(new Vector2(vertices[index].x, vertices[index].z), out float floor))
                            vertices[index].y = floor + .009f + vertices[index].y - patchPlanes[index];
                    mesh.vertices = vertices;
                    mesh.RecalculateNormals();
                }
                mesh.RecalculateBounds();
                var fence = new GameObject(name);
                fence.transform.SetParent(root, false);
                fence.AddComponent<MeshFilter>().sharedMesh = mesh;
                fence.AddComponent<MeshRenderer>().sharedMaterial = material;
                fence.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
                if (collision) fence.AddComponent<MeshCollider>().sharedMesh = mesh;
                foreach (Transform part in parts)
                {
                    part.gameObject.SetActive(false);
                    if (Application.isPlaying) Object.Destroy(part.gameObject); else Object.DestroyImmediate(part.gameObject);
                }
            }
        }

        private static Quaternion AlongGround(Vector3 direction)
        {
            Vector3 forward = Vector3.Cross(direction, Vector3.up).normalized;
            Vector3 up = Vector3.Cross(forward, direction).normalized;
            return Quaternion.LookRotation(forward, up);
        }
    }
}
