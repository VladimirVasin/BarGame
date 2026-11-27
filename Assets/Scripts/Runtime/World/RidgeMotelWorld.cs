using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    internal static class RidgeMotelWorld
    {
        internal static void Build(Transform parent, RidgeMotelPlan site, List<Collider> rainShelters = null)
        {
            var model = Resources.Load<GameObject>("City/RidgeMotel/RidgeMotel");
            var finish = Resources.Load<Material>("City/RidgeGarages/RidgeGaragesSurface");
            var glass = Resources.Load<Material>("Housing/FrontageGlass");
            if (!model || !finish || !glass) throw new InvalidOperationException("Missing Ridge motel model or finishes.");
            var root = new GameObject("Upper Street Motel").transform;
            root.SetParent(parent, false);
            root.localPosition = site.Origin;
            Transform shell = UnityEngine.Object.Instantiate(model, root, false).transform;
            foreach (var part in shell.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (part.name.StartsWith("COL_", StringComparison.Ordinal))
                {
                    part.enabled = false;
                    if (rainShelters == null) continue;
                    var collider = part.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
                    FootstepGround.Stamp(part.gameObject, part.name == "COL_Metal" ? FootstepGroundKind.Metal : FootstepGroundKind.Concrete);
                    continue;
                }
                if (part.name.StartsWith("Lens", StringComparison.Ordinal))
                {
                    string id = part.name.Substring(4);
                    var aim = part.transform.parent.Find("Aim" + id);
                    if (!aim) throw new InvalidOperationException("Missing motel lamp aim " + id);
                    Lamp(root, part, id, aim.position);
                    continue;
                }
                switch (part.name)
                {
                    case "Court":
                        CityExteriorAppearance.ApplyRoadSurface(part);
                        CityGroundSurfaceSystem.RegisterJointScenery(part);
                        break;
                    case "Parking":
                        CityExteriorAppearance.ApplyRoadMarkingSurface(part);
                        break;
                    case "Glass":
                        part.sharedMaterial = glass;
                        break;
                    case "WarmPanes":
                        Glow(part, new Color(.94f, .76f, .52f));
                        break;
                    default:
                        part.sharedMaterial = finish;
                        break;
                }
            }
            if (rainShelters == null)
            {
                shell.Find("BottleBag").gameObject.SetActive(false);
                return;
            }
            root.gameObject.AddComponent<RidgeMotelCourtLife>().Initialize(shell);
            // Each roof covers a separate walk, including the open top gallery.
            for (int floor = 0; floor < 3; floor++)
            {
                var box = CityTerracePilotWorld.Box(root, "Covered motel gallery",
                    new Vector3(1f, floor * RidgeMotelPlan.Storey + 1.45f, .94f),
                    new Vector3(28f, 2.9f, 1.86f), Quaternion.identity);
                box.isTrigger = true;
                box.gameObject.layer = 2;
                rainShelters.Add(box);
            }
        }

        private static void Glow(Renderer part, Color colour)
        {
            part.sharedMaterial = CityNightResources.EmissiveMaterial;
            part.shadowCastingMode = ShadowCastingMode.Off;
            CityNightGlowRegistry.Register(part, colour);
        }

        private static void Lamp(Transform root, Renderer lens, string id, Vector3 target)
        {
            bool gallery = id.StartsWith("Gallery", StringComparison.Ordinal);
            bool desk = id == "Desk" || id == "Window";
            // Two replacement opal covers are slightly warmer. The central
            // corridor lamps cast shorter pools, leaving soft gaps along the rail.
            bool warmCover = id == "Gallery11" || id == "Gallery22";
            Color colour = desk ? new Color(1f, .82f, .60f) : warmCover
                ? new Color(1f, .91f, .75f) : new Color(.86f, .94f, .90f);
            float strength = gallery ? (id.EndsWith("1", StringComparison.Ordinal) ? 10f : 14f)
                : id == "Window" ? 14f : id == "CourtCorner" ? 18f : desk ? 9f : id == "Entry" ? 22f : 26f;
            var light = new GameObject(id + " motel lamp").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.transform.position = lens.bounds.center + Vector3.down * .09f;
            light.transform.rotation = Quaternion.LookRotation(target - light.transform.position);
            light.type = LightType.Spot;
            light.spotAngle = gallery ? 152f : 120f;
            light.innerSpotAngle = gallery ? 108f : 72f;
            light.range = id == "CourtCorner" ? 8.5f : id == "Window" ? 6.1f : desk ? 4.5f : gallery ? 8.3f : 7.5f;
            light.color = colour;
            light.shadows = LightShadows.Soft;
            light.shadowNearPlane = .055f;
            light.shadowBias = .023f;
            light.shadowNormalBias = .075f;
            var settings = light.GetUniversalAdditionalLightData();
            settings.usePipelineSettings = false;
            if (Application.isPlaying)
                settings.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
            Color lensColour = new Color(colour.r * 1.85f, colour.g * 1.85f, colour.b * 1.85f);
            if (id == "Gallery11")
            {
                lens.sharedMaterial = CityNightResources.EmissiveMaterial;
                lens.shadowCastingMode = ShadowCastingMode.Off;
                light.gameObject.AddComponent<RidgeMotelGalleryLamp>().Initialize(light, lens, strength, lensColour);
                return;
            }
            Glow(lens, lensColour);
            CityNightSiteLightRegistry.Register(light, strength, strength * 2f / 3f, null);
        }
    }
}
