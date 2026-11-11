using System;
using UnityEngine;

namespace BarPromenade
{
    public static class CityPawnshopWorld
    {
        public const string RootName = "Pawnshop Exterior";
        public const string ResourcePath = "Pawnshop/PawnshopExterior3D";

        public static Transform Build(Transform parent, CityPawnshopPlan plan, bool interactive)
            => Build(parent, plan, interactive, out _);

        public static Transform Build(Transform parent, CityPawnshopPlan plan, bool interactive, out Collider rainShelter)
        {
            rainShelter = null;
            var template = Resources.Load<GameObject>(ResourcePath);
            if (template == null) throw new InvalidOperationException("Pawnshop exterior model is missing.");
            var root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(plan.Origin, plan.Rotation);
            var model = UnityEngine.Object.Instantiate(template, root, false);
            model.name = "Pawnshop Exterior 3D";
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                switch (part.name)
                {
                    case "ANCHOR_EntryLight": Task("PawnshopEntrance", part, plan.DoorOutward, .55f); break;
                    case "ANCHOR_SignLightLeft": SignProjector("PawnshopSignLeft", part, -.85f); break;
                    case "ANCHOR_SignLightRight": SignProjector("PawnshopSignRight", part, .85f); break;
                    case "ANCHOR_FacadeLightLeft": Task("PawnshopFacadeLeft", part, -root.right, .15f); break;
                    case "ANCHOR_FacadeLightRight": Task("PawnshopFacadeRight", part, root.right, .15f); break;
                    case "ANCHOR_FacadeLightRear": Task("PawnshopFacadeRear", part, root.forward, .15f); break;
                }
            }
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
                if (renderer.name == "PART_EntrancePractical" ||
                    renderer.name.StartsWith("PART_FacadePractical", StringComparison.Ordinal) ||
                    renderer.name.StartsWith("PART_SignPractical", StringComparison.Ordinal))
                {
                    renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    CityNightGlowRegistry.Register(renderer, new Color(1.65f, 1.72f, 1.55f));
                }
            // One solid envelope is sufficient in City: all playable interior
            // space belongs to the separate scene. Visual geometry is imported.
            if (interactive)
            {
                var solid = root.gameObject.AddComponent<BoxCollider>();
                solid.center = new Vector3(0f, (CityPawnshopPlan.Height - .35f) * .5f, 0f);
                solid.size = new Vector3(CityPawnshopPlan.Width, CityPawnshopPlan.Height + .35f, CityPawnshopPlan.Depth);
                var canopy = new GameObject("Pawnshop Canopy Support");
                canopy.transform.SetParent(root, false);
                canopy.transform.localPosition = new Vector3(0f, 2.59f, -4.91f);
                var roof = canopy.AddComponent<BoxCollider>();
                roof.size = new Vector3(2.1f, .10f, 1.02f);
                rainShelter = roof;
                var target = new GameObject("Pawnshop Entrance");
                target.transform.SetParent(root, false);
                target.transform.position = plan.EntrancePosition;
                var trigger = target.AddComponent<SphereCollider>();
                trigger.isTrigger = true; trigger.radius = 1.05f;
                target.AddComponent<PlayerDoorActionTarget>().Configure(plan.DoorAction);
                target.AddComponent<PawnshopEntrance>().Configure(plan);
            }
            return root;

            void SignProjector(string name, Transform anchor, float targetX)
            {
                // Two shielded projectors overlap across the matte board. Their
                // physical lenses face the sign; there is no camera-facing halo.
                Vector3 target = root.TransformPoint(new Vector3(targetX, 3.15f, -4.64f));
                Vector3 direction = (target - anchor.position).normalized;
                var lamp = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + name).transform;
                lamp.SetParent(root, false);
                lamp.position = anchor.position + direction * .045f;
                lamp.rotation = Quaternion.LookRotation(direction, root.up);
            }

            void Task(string name, Transform anchor, Vector3 outward, float outwardAim)
            {
                // The sources sit just below their real frosted diffusers and
                // clear of the wall. Their wide cones wash the facade and island
                // through the existing nearest-fixture pool, never a global fill.
                var lamp = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + name).transform;
                lamp.SetParent(root, false);
                lamp.position = anchor.position + outward * .08f - root.up * .04f;
                lamp.rotation = Quaternion.LookRotation((-root.up + outward * outwardAim).normalized, outward);
            }
        }
    }
}
