using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal sealed class DistrictParkingWorld : MonoBehaviour
    {
        private readonly List<Mesh> gradedMeshes = new(3);
        internal DistrictParkingPlan Plan { get; private set; }

        internal static void Build(Transform parent, CityLayout layout)
        {
            var sites = DistrictParkingPlan.ForLayout(layout);
            if (sites.Count == 0) return;
            var asset = Resources.Load<GameObject>(DistrictParkingPlan.AssetPath);
            if (!asset) throw new InvalidOperationException("Missing authored district parking.");
            foreach (var site in sites)
            {
                var root = new GameObject(site.Id);
                root.transform.SetParent(parent, false);
                root.transform.SetPositionAndRotation(site.Origin, site.Rotation);
                var result = root.AddComponent<DistrictParkingWorld>();
                result.Plan = site;
                result.BuildModel(Instantiate(asset, root.transform, false).transform);
            }
        }

        private void BuildModel(Transform model)
        {
            Transform frame = transform;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
            {
                Transform part = filter.transform;
                Mesh mesh = Instantiate(filter.sharedMesh);
                gradedMeshes.Add(mesh);
                var vertices = mesh.vertices;
                // Account for the imported FBX metre conversion before applying the grade.
                Matrix4x4 toSite = frame.worldToLocalMatrix * part.localToWorldMatrix;
                Matrix4x4 toPart = toSite.inverse;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 point = toSite.MultiplyPoint3x4(vertices[i]);
                    point.y += Plan.Top(point.x, point.z) - Plan.Origin.y;
                    vertices[i] = toPart.MultiplyPoint3x4(point);
                }
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                filter.sharedMesh = mesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (part.name == "Markings")
                {
                    CityExteriorAppearance.ApplyRoadMarkingSurface(renderer);
                    continue;
                }

                if (part.name == "Curbs") CityExteriorAppearance.ApplySidewalkSurface(renderer);
                else
                {
                    CityExteriorAppearance.ApplyRoadSurface(renderer);
                    CityGroundSurfaceSystem.RegisterJointScenery(renderer);
                }
                part.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
                FootstepGround.Stamp(part.gameObject, FootstepGroundKind.Concrete);
            }
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in gradedMeshes)
            {
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
        }
    }
}
