using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class RidgeKioskWorld
    {
        public static Transform Build(Transform parent, RidgeKioskSitePlan site, bool interactive) =>
            Build(parent, site, interactive, out _);

        internal static Transform Build(Transform parent, RidgeKioskSitePlan site, bool interactive, out Collider[] rainShelters)
        {
            rainShelters = Array.Empty<Collider>();
            var root = new GameObject("Ridge Kiosk").transform;
            root.SetParent(parent, false);
            root.localPosition = site.Origin;
            root.localRotation = site.Rotation;
            var template = Resources.Load<GameObject>("RidgeKiosk/RidgeKiosk");
            if (template == null) throw new InvalidOperationException("Ridge kiosk model is missing.");
            Transform model = UnityEngine.Object.Instantiate(template, root, false).transform;
            model.localPosition = Vector3.zero;
            var anchors = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var part in model.GetComponentsInChildren<Transform>(true))
                if (part.name.StartsWith("Kiosk", StringComparison.Ordinal)) anchors[part.name] = part;

            Transform approach = anchors["KioskApproach"];
            RidgeKioskOffers.Build(root, anchors["KioskBeer"], anchors["KioskVodka"], anchors["KioskPie"], approach, interactive);
            // Direction is owned by this metre-space frame, not by the imported
            // Empty's local axes. Both City and the passive balcony share its lens.
            var lamp = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + "RidgeKiosk").transform;
            lamp.SetParent(root, false);
            lamp.position = anchors["KioskLamp"].position;
            lamp.rotation = root.rotation * Quaternion.LookRotation(new Vector3(0f, -1f, .32f), Vector3.forward);

            if (interactive)
            {
                rainShelters = BuildCollision(root);
                RidgeKioskVendor.Build(root, anchors["KioskVendor"], anchors["KioskTalk"], approach);
            }
            return root;
        }

        private static Collider[] BuildCollision(Transform root)
        {
            BoxCollider Box(string part, Vector3 centre, Vector3 size) =>
                CityTerracePilotWorld.Box(root, part, centre, size, Quaternion.identity);
            Box("Rear wall", new Vector3(0f, 1.16f, -1.20f), new Vector3(3f, 2.75f, .10f));
            foreach (float sign in new[] { -1f, 1f })
            {
                Box("Side wall", new Vector3(sign * 1.46f, 1.16f, 0f), new Vector3(.08f, 2.75f, 2.5f));
                Box("Window jamb", new Vector3(sign * 1.31f, 1.58f, 1.20f), new Vector3(.38f, 1.08f, .10f));
            }
            Box("Counter base", new Vector3(0f, .275f, 1.20f), new Vector3(3f, 1.45f, .10f));
            Box("Counter", new Vector3(0f, 1.02f, 1.21f), new Vector3(2.48f, .04f, .594f));
            Box("Window head", new Vector3(0f, 2.32f, 1.20f), new Vector3(3f, .40f, .10f));
            var roof = Box("Roof", new Vector3(0f, 2.55f, 0f), new Vector3(3.14f, .10f, 2.68f));
            var hood = CityTerracePilotWorld.Box(root, "Window hood", new Vector3(0f, 2.255f, 1.475f),
                new Vector3(2.74f, .051f, .60f), Quaternion.Euler(8f, 0f, 0f));
            Box("Service floor", new Vector3(0f, -.185f, 0f), new Vector3(3f, .53f, 2.5f));
            float front = RidgeKioskSitePlan.ApronFront, back = RidgeKioskSitePlan.ApronBack;
            var apron = Box("Visitor apron", new Vector3(0f, -.25f, (front + back) * .5f),
                new Vector3(3f, .5f, front - back));
            FootstepGround.Stamp(apron.gameObject, FootstepGroundKind.Concrete);
            return new Collider[] { roof, hood };
        }
    }
}
