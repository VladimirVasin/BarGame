using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>Places imported topology and fits it to the existing ground; creates no visible primitives.</summary>
    public static class CityEastExitDressingWorldBuilder
    {
        public const string ResourcePath = "City/EastExit/CityEastExitDressing3D";
        public const string RootName = "Eastern Checkpoint Surroundings";

        internal static void AddTemplates(IDictionary<string, Transform> templates)
        {
            GameObject asset = Resources.Load<GameObject>(ResourcePath);
            if (asset == null) throw new InvalidOperationException("Missing authored checkpoint surroundings kit: " + ResourcePath);
            foreach (Transform item in asset.GetComponentsInChildren<Transform>(true))
                if (!templates.ContainsKey(item.name)) templates.Add(item.name, item);
        }

        internal static Transform Build(Transform parent, CityEastExitPlan exit, CityEastExitDressingPlan plan,
            IDictionary<string, Transform> templates)
        {
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            var groups = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (CityEastExitDressingPart part in plan.Parts)
            {
                if (!groups.TryGetValue(part.GroupId, out Transform group))
                {
                    group = new GameObject(part.GroupId).transform;
                    group.SetParent(root, false); groups.Add(part.GroupId, group);
                }
                Transform placed = CityEastExitWorldBuilder.Place(templates, group, part.Assembly, part.Id,
                    part.Position, part.Rotation, part.Scale);
                FitAuthoredMeshes(placed, exit, part);
                if (part.GroupId == "Post Foot Traces")
                    foreach (Renderer renderer in placed.GetComponentsInChildren<Renderer>(true))
                        CityFringeYardSurfaceAppearance.ApplyCombined(renderer, CityFringeYardSurfaceKind.ForefieldGround,
                            new Color(.285f, .30f, .245f));
                if (part.Assembly == "RoadRepair")
                    foreach (Renderer renderer in placed.GetComponentsInChildren<Renderer>(true))
                    {
                        var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                        block.SetColor("_BaseColor", new Color(.23f, .24f, .21f));
                        block.SetColor("_Color", new Color(.23f, .24f, .21f)); renderer.SetPropertyBlock(block);
                    }
                if (part.Assembly == "GroundRidge")
                {
                    foreach (MeshFilter filter in placed.GetComponentsInChildren<MeshFilter>(true))
                        filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                    FootstepGround.Stamp(placed.gameObject, FootstepGroundKind.Soil);
                }
                else
                    foreach (CityEastExitDressingSolid solid in plan.Solids)
                        if (solid.PartId == part.Id)
                        {
                            BoxCollider collider = placed.gameObject.AddComponent<BoxCollider>();
                            collider.center = placed.InverseTransformPoint(solid.Position);
                            collider.size = new Vector3(solid.Size.x / part.Scale.x, solid.Size.y / part.Scale.y, solid.Size.z / part.Scale.z);
                        }
                if (part.Assembly == "Shelter")
                    CityEastExitWorldBuilder.AddBox(placed, new Vector3(0, 2.57f, 0), new Vector3(3.6f, .16f, 3.2f));
            }
            return root;
        }

        private static void FitAuthoredMeshes(Transform placement, CityEastExitPlan exit, CityEastExitDressingPart part)
        {
            foreach (MeshFilter filter in placement.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh source = filter.sharedMesh;
                if (source == null || !source.isReadable)
                    throw new InvalidOperationException("Checkpoint ground fitting requires readable authored mesh: " + filter.name);
                Mesh mesh = Object.Instantiate(source);
                mesh.name = part.Id + " Ground Fitted " + source.name;
                Vector3[] vertices = mesh.vertices;
                bool post = part.Assembly == "Shelter" && filter.name.IndexOf("_Post", StringComparison.Ordinal) >= 0;
                bool foot = part.Assembly == "Shelter" && filter.name.IndexOf("_Foot", StringComparison.Ordinal) >= 0;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 world = filter.transform.TransformPoint(vertices[i]);
                    var point = new Vector2(world.x, world.z);
                    float authoredHeight = world.y - part.Position.y;
                    float ground = exit.RoadBounds.Contains(point) ? exit.SampleRoadTop(point.x) : exit.SampleGroundTop(point);
                    if (part.Fit == CityEastExitDressingFit.Ground || part.Fit == CityEastExitDressingFit.Road)
                        world.y = ground + authoredHeight;
                    else
                    {
                        float weight = foot ? 1f : Mathf.Clamp01(1f - authoredHeight / (post ? 2.48f : .30f));
                        world.y += (ground - part.Position.y) * weight;
                    }
                    vertices[i] = filter.transform.InverseTransformPoint(world);
                }
                mesh.vertices = vertices;
                mesh.RecalculateBounds(); mesh.RecalculateNormals();
                filter.sharedMesh = mesh;
                filter.gameObject.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            }
        }
    }
}
