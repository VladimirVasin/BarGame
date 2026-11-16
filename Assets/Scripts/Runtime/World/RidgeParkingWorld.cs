using System;
using UnityEngine;

namespace BarPromenade
{
    internal sealed class RidgeParkingWorld : MonoBehaviour
    {
        internal RidgeParkingPlan Plan { get; private set; }
        internal LastRouteCarAssetRegistry ParkedCar { get; private set; }

        internal static RidgeParkingWorld Build(Transform parent, CityLayout layout, bool interactive)
        {
            var plan = RidgeParkingPlan.ForLayout(layout);
            if (plan == null) return null;
            var root = new GameObject("Ridge four-space parking").transform;
            root.SetParent(parent, false);
            root.localPosition = plan.Origin;
            var result = root.gameObject.AddComponent<RidgeParkingWorld>();
            result.Plan = plan;
            var source = Resources.Load<GameObject>("City/RidgeParking/RidgeParking");
            if (!source) throw new InvalidOperationException("Missing authored Ridge parking.");
            Transform model = Instantiate(source, root, false).transform;
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.name == "Markings") CityExteriorAppearance.ApplyRoadMarkingSurface(renderer);
                else if (renderer.name == "Curbs" || renderer.name == "Apron") CityExteriorAppearance.ApplySidewalkSurface(renderer);
                else CityExteriorAppearance.ApplyRoadSurface(renderer);
                if (renderer.name != "Curbs" && renderer.name != "Markings") CityGroundSurfaceSystem.RegisterJointScenery(renderer);
                if (interactive && renderer.name != "Markings")
                {
                    var solid = renderer.gameObject.AddComponent<MeshCollider>();
                    solid.sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    FootstepGround.Stamp(solid.gameObject, FootstepGroundKind.Concrete);
                }
            }
            // The normal sedan, with no driver, passenger interaction or owner.
            // RideOnly rests completely dark; its engine has never been started.
            result.ParkedCar = LastRouteCarFactory.Create(root,
                LastRouteCarPlan.At(plan.ParkedPosition(RidgeParkingPlan.OccupiedBay), Vector3.back),
                default, null, LastRouteCarLamps.RideOnly, ambient: true);
            if (!result.ParkedCar) throw new InvalidOperationException("Missing parked sedan.");
            var car = result.ParkedCar.GetComponentInParent<LastRouteCarDriver>();
            car.name = "Parked Ridge Sedan";
            if (!interactive)
                foreach (var collider in car.GetComponentsInChildren<Collider>())
                {
                    collider.enabled = false;
                    if (Application.isPlaying) Destroy(collider);
                    else DestroyImmediate(collider);
                }
            return result;
        }
    }
}
