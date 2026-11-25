using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    internal static class RidgeGarageWorld
    {
        internal static Collider Build(Transform parent, RidgeGaragePlan site, bool interactive)
        {
            var root = new GameObject("West Ridge Garages").transform;
            root.SetParent(parent, false);
            root.localPosition = site.Origin;
            FootstepGround.Stamp(root.gameObject, FootstepGroundKind.Stone);
            var template = Resources.Load<GameObject>(RidgeGaragePlan.ResourcePath);
            var surface = Resources.Load<Material>("City/RidgeGarages/RidgeGaragesSurface");
            if (!template || !surface) throw new InvalidOperationException("Missing Ridge garage model or surface.");
            var shell = UnityEngine.Object.Instantiate(template, root, false).transform;
            foreach (var part in shell.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (part.name.StartsWith("COL_", StringComparison.Ordinal))
                {
                    part.enabled = false;
                    if (!interactive) continue;
                    var b = part.GetComponent<MeshFilter>().sharedMesh.bounds;
                    var solid = part.gameObject.AddComponent<BoxCollider>();
                    solid.size = b.size;
                    solid.center = b.center;
                }
                else part.sharedMaterial = surface;
            }

            var anchors = shell.GetComponentsInChildren<Transform>(true);
            // The suspended opal diffuser reaches the walls and ceiling.
            // The shaded fixtures bring out the engine, bench and doorway hardware.
            AddFixture("Ceiling", "GEO_CeilingLens", new Color(1f, .89f, .73f), 3.1f, 6.5f, .24f);
            AddSpot("Work", "GEO_Lens", new Color(1f, .80f, .58f), 7.8f, 4.8f, 148f, 86f, .26f);
            AddSpot("Inspection", "GEO_InspectionLens", new Color(1f, .92f, .76f), 4.8f, 4.4f, 142f, 80f, .14f);
            AddSpot("Entry", "GEO_EntryLens", new Color(.80f, .90f, .87f), 23f, 10f, 156f, 104f, .48f);
            BuildStairs(root, site);
            if (!interactive) return null;
            var shelter = CityTerracePilotWorld.Box(root, "Garage workshop rain shelter", new Vector3(5.25f, 1.30f, -3f),
                new Vector3(3.48f, 2.70f, 6f), Quaternion.identity);
            shelter.isTrigger = true;
            shelter.gameObject.layer = 2;
            return shelter;

            void AddSpot(string id, string lensName, Color color, float intensity, float range,
                float outerAngle, float innerAngle, float haloDiameter)
            {
                var light = AddFixture(id, lensName, color, intensity, range, haloDiameter);
                light.type = LightType.Spot;
                light.transform.rotation = Quaternion.LookRotation(Anchor("ANCHOR_" + id + "Aim").position - light.transform.position);
                light.spotAngle = outerAngle;
                light.innerSpotAngle = innerAngle;
            }

            Light AddFixture(string id, string lensName, Color color, float intensity, float range, float haloDiameter)
            {
                Transform source = Anchor("ANCHOR_" + id + "Light");
                var lens = Anchor(lensName).GetComponent<MeshRenderer>();
                lens.sharedMaterial = CityNightResources.EmissiveMaterial;
                lens.shadowCastingMode = ShadowCastingMode.Off;
                CityNightGlowRegistry.Register(lens, new Color(2.15f * color.r, 2.15f * color.g, 2.15f * color.b));

                var light = new GameObject("Garage " + id + " light").AddComponent<Light>();
                light.transform.SetParent(root, false);
                light.transform.position = source.position;
                light.type = LightType.Point;
                light.range = range;
                light.color = color;
                light.shadows = LightShadows.Soft;
                var settings = light.GetUniversalAdditionalLightData();
                settings.usePipelineSettings = false;
                if (Application.isPlaying)
                    settings.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                light.shadowNearPlane = .06f;
                light.shadowBias = .025f;
                light.shadowNormalBias = .09f;
                var halo = CityLightHalo.CreateAlwaysBurning(light.transform, Vector3.zero,
                    haloDiameter * .32f, haloDiameter,
                    new Color(color.r, color.g, color.b, .16f), new Color(color.r, color.g, color.b, .035f));
                CityNightSiteLightRegistry.Register(light, intensity, halo);
                return light;
            }

            Transform Anchor(string name) => Array.Find(anchors, t => t.name == name) ??
                throw new InvalidOperationException("Missing garage fixture part " + name);
        }

        private static void BuildStairs(Transform root, RidgeGaragePlan site)
        {
            const string folder = CityTerracePilotWorld.ResourceFolder;
            var flightMesh = Resources.Load<GameObject>(folder + "TerraceFlight");
            var support = Resources.Load<GameObject>(folder + "TerraceSupport");
            var deck = Resources.Load<GameObject>(folder + "TerraceCourt");
            var rail = Resources.Load<GameObject>(folder + "TerraceRail");
            foreach (var flight in site.Flights)
                CityTerracePilotWorld.BuildFlight(root, flight, flightMesh, support, -.35f,
                    flight.StepRise / CityTerracePilotPlan.StepRise);
            Landing(site.Flights[0].End, 1.2f, true);
            Landing(site.Flights[1].End, 1.85f, false);

            void Landing(Vector3 start, float length, bool twoSides)
            {
                const float width = 2.1f;
                Vector3 centre = start + Vector3.back * (length * .5f);
                CityTerracePilotWorld.Model(deck, root, "Garage stair landing", centre, Quaternion.identity,
                    new Vector3(width / 10f, 1f, length / 10f));
                CityTerracePilotWorld.Box(root, "Garage landing floor", centre + Vector3.down * .15f,
                    new Vector3(width, .30f, length), Quaternion.identity);
                CityTerracePilotWorld.Support(support, root, "Garage landing foundation", centre + Vector3.down * .29f,
                    new Vector2(width, length), -.35f);
                Vector3 outer = start + Vector3.right * .99f, inner = start + Vector3.left * .99f;
                CityTerracePilotWorld.BuildRailRun(root, rail, "Garage landing outer rail", outer, outer + Vector3.back * length);
                if (twoSides) CityTerracePilotWorld.BuildRailRun(root, rail, "Garage landing inner rail", inner, inner + Vector3.back * length);
                else CityTerracePilotWorld.BuildRailRun(root, rail, "Garage stair end rail", inner + Vector3.back * length, outer + Vector3.back * length);
            }
        }
    }
}
