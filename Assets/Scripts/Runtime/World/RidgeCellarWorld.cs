using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    internal static class RidgeCellarWorld
    {
        private const string Folder = "City/RidgeCellars/";

        internal static void Build(Transform parent, CityLayout layout, RidgeCellarPlan site, bool physical)
        {
            var root = new GameObject("East Ridge Cellar Court").transform;
            root.SetParent(parent, false);
            root.localPosition = site.Origin;
            var finish = Resources.Load<Material>("City/RidgeGarages/RidgeGaragesSurface");
            if (!finish) throw new InvalidOperationException("Missing cellar court finish.");
            var shell = UnityEngine.Object.Instantiate(Model("RidgeCellars"), root, false);
            DressFixtures(shell, root, finish, physical);

            var tread = Model("CellarTread");
            for (int i = 0; i < RidgeCellarPlan.Steps; i++)
            {
                var foot = site.StairFoot + Vector2.up * (i * RidgeCellarPlan.Tread);
                var step = UnityEngine.Object.Instantiate(tread, root, false);
                // The ground grid carries the continuous stair ramp beneath the nosings.
                step.transform.localPosition = new Vector3(foot.x - site.Origin.x,
                    (i + 1) * RidgeCellarPlan.Rise + .003f, foot.y - site.Origin.z);
                Dress(step, finish);
            }

            var ground = CityHillsideGroundPlan.ForLayout(layout);
            var laneLamp = UnityEngine.Object.Instantiate(Model("CellarLaneLamp"), root, false);
            laneLamp.transform.localPosition = LocalFoot(site.LaneLampFoot) - Vector3.up * .025f;
            // The imported root carries the authored up-axis conversion. Apply the
            // site bearing to that basis, as for the cast covers along the grade.
            var laneBearing = Quaternion.LookRotation(new Vector3(site.LaneLampForward.x, 0f, site.LaneLampForward.y));
            laneLamp.transform.localRotation = laneBearing * laneLamp.transform.localRotation;
            DressFixtures(laneLamp, root, finish, physical);
            var drain = Model("CellarDrain");
            var grate = Model("CellarGrate");
            for (int i = 1; i < site.Drain.Count; i++)
            {
                Vector3 first = LocalFoot(site.Drain[i - 1]), last = LocalFoot(site.Drain[i]);
                Vector3 delta = last - first;
                var segment = new GameObject("Drain section").transform;
                segment.SetParent(root, false);
                segment.localPosition = first;
                segment.localRotation = Quaternion.LookRotation(delta);
                float taper = i == 1 || i == site.Drain.Count - 1 ? .25f :
                    i == 2 || i == site.Drain.Count - 2 ? .64f : 1f;
                segment.localScale = new Vector3(1f, taper, delta.magnitude + .024f);
                Dress(UnityEngine.Object.Instantiate(drain, segment, false), finish);
                if (i == 3 || i == site.Drain.Count - 4)
                {
                    // Keep the grating bars at their authored pitch on the longer cast sections.
                    var cover = UnityEngine.Object.Instantiate(grate, root, false);
                    cover.transform.localPosition = first + delta * .42f;
                    cover.transform.localRotation = segment.localRotation * cover.transform.localRotation;
                    Dress(cover, finish);
                    if (physical)
                    {
                        var mouth = cover.transform.localPosition + segment.localRotation * new Vector3(-.31f, .04f, .325f);
                        CityRainRunoffSite.AddInlet(root, root.TransformPoint(mouth), segment.forward, .16f, .32f);
                    }
                }
                if (physical)
                {
                    var solid = segment.gameObject.AddComponent<BoxCollider>();
                    solid.center = new Vector3(0f, .20f, .5f);
                    solid.size = new Vector3(.26f, .40f, 1f);
                }
            }

            if (physical) RidgeCellarKeeper.Build(root, layout, site);

            Vector3 LocalFoot(Vector2 point)
            {
                if (!ground.TrySampleTop(point, out float top))
                    throw new InvalidOperationException("Missing ground below the cellar drain at " + point);
                return new Vector3(point.x, top + .005f, point.y) - site.Origin;
            }
        }

        private static void DressFixtures(GameObject model, Transform court, Material finish, bool physical)
        {
            foreach (var part in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                switch (part.name)
                {
                    case "LensWork":
                        BuildLamp(court, part, "Work", new Color(1f, .78f, .54f), 40f, 10f, 112f, 68f, .36f);
                        break;
                    case "LensEntry":
                        BuildLamp(court, part, "Entry", new Color(.81f, .88f, .86f), 51f, 12.5f, 104f, 62f, .39f);
                        break;
                    case "LensWall":
                        BuildLamp(court, part, "Wall", new Color(.96f, .86f, .72f), 150f, 17.5f, 88f, 55f, .32f);
                        break;
                    case "LensLane":
                        BuildLamp(court, part, "Lane", new Color(.98f, .86f, .68f), 64f, 13.5f, 113f, 72f, .55f);
                        break;
                    case "EarthBank":
                        CityExteriorAppearance.ApplyGroundSurface(part, CityHillsidePerimeterSurface.ResidentialSoil);
                        break;
                    default:
                        if (part.name.StartsWith("COL_", StringComparison.Ordinal))
                        {
                            part.enabled = false;
                            if (physical)
                            {
                                part.gameObject.AddComponent<MeshCollider>().sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
                                FootstepGround.Stamp(part.gameObject, FootstepGroundKind.Concrete);
                            }
                        }
                        else part.sharedMaterial = finish;
                        break;
                }
            }
        }

        private static void BuildLamp(Transform court, MeshRenderer lens, string id, Color colour,
            float intensity, float range, float outerAngle, float innerAngle, float haloSize)
        {
            Transform source = lens.transform.parent.Find("Light" + id);
            Transform aim = lens.transform.parent.Find("Aim" + id);
            if (!source || !aim) throw new InvalidOperationException("Incomplete cellar fixture " + id);
            lens.sharedMaterial = CityNightResources.EmissiveMaterial;
            lens.shadowCastingMode = ShadowCastingMode.Off;
            CityNightGlowRegistry.Register(lens, new Color(colour.r * 2f, colour.g * 2f, colour.b * 2f));
            var spot = new GameObject("Cellar " + id + " light").AddComponent<Light>();
            spot.transform.SetParent(court, false);
            spot.transform.SetPositionAndRotation(source.position, Quaternion.LookRotation(aim.position - source.position));
            spot.type = LightType.Spot;
            spot.color = colour;
            spot.range = range;
            spot.spotAngle = outerAngle;
            spot.innerSpotAngle = innerAngle;
            spot.shadows = LightShadows.Soft;
            spot.shadowNearPlane = .055f;
            spot.shadowBias = .022f;
            spot.shadowNormalBias = .07f;
            var settings = spot.GetUniversalAdditionalLightData();
            settings.usePipelineSettings = false;
            if (Application.isPlaying)
                settings.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
            var halo = CityLightHalo.CreateAlwaysBurning(spot.transform, Vector3.zero, .11f, haloSize,
                new Color(colour.r, colour.g, colour.b, .14f), new Color(colour.r, colour.g, colour.b, .034f));
            CityNightSiteLightRegistry.Register(spot, intensity, halo);
        }

        private static GameObject Model(string name) => Resources.Load<GameObject>(Folder + name) ??
            throw new InvalidOperationException("Missing cellar court model " + name);

        private static void Dress(GameObject model, Material finish)
        {
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true)) renderer.sharedMaterial = finish;
        }
    }
}
