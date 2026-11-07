using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public static class CityPigeonLoftWorld
    {
        public static Transform Build(Transform parent, CityLayout layout, bool live = true, Rect? retained = null)
        {
            var plan = CityPigeonLoftPlan.ForLayout(layout);
            if (!plan.IsEnabled || (retained.HasValue && !retained.Value.Overlaps(plan.Courtyard.Bounds))) return null;
            Transform bath = CityPigeonCourtyardWorld.Build(parent, layout, live, out var gate);
            var root = new GameObject("Ridge Pigeon Loft").transform;
            root.SetParent(parent, false);
            root.localPosition = plan.Origin;
            root.localRotation = plan.Rotation;
            var prefab = Resources.Load<GameObject>("City/PigeonLoft/PigeonLoft");
            if (prefab == null) throw new InvalidOperationException("Missing imported Ridge pigeon loft model.");
            Transform model = UnityEngine.Object.Instantiate(prefab, root, false).transform;
            model.localPosition = Vector3.zero;
            var anchors = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var perches = new List<Transform>();
            foreach (var part in model.GetComponentsInChildren<Transform>(true))
            {
                anchors[part.name] = part;
                if (part.name.StartsWith("Perch", StringComparison.Ordinal)) perches.Add(part);
            }
            perches.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            // Imported legs have their pivot at the fixed deck underside. Extend
            // each foot to the real terrain without moving the cabin or contacts.
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            foreach (var perch in perches)
                if (perch.name.StartsWith("PerchGround", StringComparison.Ordinal) &&
                    ground.TrySampleTop(new Vector2(perch.position.x, perch.position.z), out float perchTop))
                    perch.position = new Vector3(perch.position.x, perchTop + .012f, perch.position.z);

            foreach (var part in anchors.Values)
            {
                if (!part.name.StartsWith("Support", StringComparison.Ordinal)) continue;
                Vector2 foot = new(part.position.x, part.position.z);
                if (!ground.TrySampleTop(foot, out float top)) continue;
                Vector3 scale = part.localScale;
                Vector3 vertical = part.InverseTransformDirection(Vector3.up);
                int axis = Mathf.Abs(vertical.x) > Mathf.Abs(vertical.y) ? 0 : 1;
                if (Mathf.Abs(vertical.z) > Mathf.Abs(vertical[axis])) axis = 2;
                scale[axis] *= Mathf.Max(.15f, part.position.y - top + .06f) / 1.93f;
                part.localScale = scale;
            }
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.name == "LampLens" || renderer.name == "LampAviaryLens")
                {
                    renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    CityNightGlowRegistry.Register(renderer, new Color(2.8f, 2.25f, 1.5f));
                }
                else if (live && (renderer.name == "LoftStructure" || renderer.name.StartsWith("Support", StringComparison.Ordinal)))
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null)
                    {
                        renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                        FootstepGround.Stamp(renderer.gameObject, FootstepGroundKind.Wood);
                    }
                }
            }
            var lamp = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + "PigeonLoft").transform;
            lamp.SetParent(root, false);
            lamp.position = anchors["LampAnchor"].position;
            lamp.rotation = root.rotation * Quaternion.LookRotation(new Vector3(0f, -1f, -.6f), Vector3.forward);
            var aviaryLamp = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + "PigeonLoftAviary").transform;
            aviaryLamp.SetParent(root, false);
            aviaryLamp.position = anchors["LampAviaryAnchor"].position;
            aviaryLamp.rotation = root.rotation * Quaternion.LookRotation(new Vector3(-.47f, -1f, 0f), Vector3.forward);
            if (!live && anchors.TryGetValue("WaterStream", out var stream)) stream.gameObject.SetActive(false);
            if (live)
            {
                var keeper = root.gameObject.AddComponent<CityPigeonKeeper>();
                keeper.Initialize(root, anchors["Feeder"], anchors["Water"],
                    anchors["Scoop"], anchors["KeeperWorkAnchor"]);
                keeper.ConfigureCommute(CityPigeonKeeperPlan.ForLayout(layout), gate);
                Transform courtyardRoot = bath.parent;
                keeper.ConfigureDayProps(
                    CityPedestrianHandProps.FindSocket(courtyardRoot, "ViceHandle"),
                    CityPedestrianHandProps.FindSocket(courtyardRoot, "GrainLid"), bath,
                    courtyardRoot.Find("Workbench"));
                Transform bathPerch = null, bathDrink = null;
                foreach (Transform point in bath.GetComponentsInChildren<Transform>())
                {
                    if (point.name == "PerchBath") bathPerch = point;
                    if (point.name == "BathDrink") bathDrink = point;
                }
                if (!bathPerch || !bathDrink) throw new InvalidOperationException("Courtyard bath requires authored bird contacts.");
                CityPigeonFlock.Create(root, perches.ToArray(), anchors["FeederPerch"],
                    anchors["WaterPerch"], () => keeper.PresentationObserver, () => keeper.IsFeeding, bathPerch, bathDrink);
            }
            if (live)
            {
                AddRainShelter(root, new Vector3(0f, 3.92f, 0f), new Vector3(3.16f, .12f, 2.78f));
                AddRainShelter(root, new Vector3(1.74f, 3.82f, 0f), new Vector3(.48f, .12f, 2.2f));
            }
            return root;
        }
        private static void AddRainShelter(Transform root, Vector3 center, Vector3 size)
        {
            var host = new GameObject("Pigeon loft rain shelter");
            host.transform.SetParent(root, false);
            var shelter = host.AddComponent<BoxCollider>();
            shelter.center = center;
            shelter.size = size;
            shelter.isTrigger = true;
        }
    }
}
