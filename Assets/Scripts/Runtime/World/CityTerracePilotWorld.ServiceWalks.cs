using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    public static partial class CityTerracePilotWorld
    {
        internal static void BuildServiceWalks(Transform parent, CityLayout layout)
        {
            var routes = CityServiceStairPlan.Lowland(layout);
            if (routes.Count == 0) return;
            FootProbeSurface.EnsureRuntimePolicy();
            var flightModel = Template("ServiceFlight");
            var support = Template("TerraceSupport");
            var deck = Template("TerraceLanding");
            foreach (var stair in routes)
            {
                var root = new GameObject(stair.Id).transform;
                root.SetParent(parent, false);
                FootstepGround.Stamp(root.gameObject, FootstepGroundKind.Stone);
                var flight = stair.Flights[0];
                float baseY = stair.FoundationBottom;
                BuildFlight(root, flight, flightModel, support, baseY);
                foreach (var floor in stair.Floors)
                {
                    Vector3 top = floor.Center;
                    var scale = new Vector3(floor.Size.x / 2.4f, 1f, floor.Size.y / 2.4f);
                    Model(deck, root, floor.Id, top, Quaternion.identity, scale, true,
                        new Vector4(scale.x, scale.z, top.x * .5f, top.z * .5f));
                    Box(root, floor.Id + " surface", top - Vector3.up * .125f,
                        new Vector3(floor.Size.x, .25f, floor.Size.y), Quaternion.identity);
                    Support(support, root, floor.Id + " base", top - Vector3.up * .24f, floor.Size, baseY);
                }
                foreach (var apron in stair.Aprons)
                {
                    Vector3 delta = apron.End - apron.Start, mid = (apron.Start + apron.End) * .5f;
                    Quaternion rotation = Quaternion.LookRotation(delta);
                    var scale = new Vector3(CityHillsideShortcut.Width / 2.4f, 1f, delta.magnitude / 2.4f);
                    Model(deck, root, "Service approach", mid, rotation, scale, true,
                        new Vector4(scale.x, scale.z, 0, 0));
                    Box(root, "Approach surface", mid - rotation * Vector3.up * .125f,
                        new Vector3(CityHillsideShortcut.Width, .25f, delta.magnitude), rotation);
                    // Solid fascia follows the sloping deck all the way into the ground.
                    Model(support, root, "Approach footing", mid - rotation * Vector3.up * .24f, rotation,
                        new Vector3(CityHillsideShortcut.Width / 2.4f, .75f, delta.magnitude / 2.4f));
                }
                if (stair.Id == CityHillsideShortcut.Workshop)
                {
                    Vector3 lip = flight.End + flight.Direction * .1f + Vector3.forward * 1.045f;
                    Fitting(root, "LoadingEdge", lip, Quaternion.LookRotation(Vector3.left));
                    var warm = new Color(1f, .77f, .51f);
                    LightFlight(root, flight, warm, 36f);
                    var apron = stair.Aprons[0];
                    StairLamp(root, apron.Start + Vector3.left * .91f, Vector3.right,
                        Vector3.Lerp(apron.Start, apron.End, .34f), warm, 42f);
                }
                else
                {
                    var grate = Fitting(root, "OutletShelf", flight.Start + Vector3.forward * 1.05f,
                        Quaternion.identity);
                    foreach (var r in grate.GetComponentsInChildren<Renderer>())
                        if (r.name.StartsWith("Outfall masonry", StringComparison.Ordinal))
                            CityFringeYardSurfaceAppearance.ApplyCombined(r, CityFringeYardSurfaceKind.Masonry,
                                new Color(.48f, .50f, .46f));
                    var pitch = stair.Aprons[1];
                    var edge = Vector3.left * .99f;
                    BuildRailRun(root, Template("TerraceRail"), "Inspection bank rail", pitch.Start + edge, pitch.End + edge);
                    LightFlight(root, flight, new Color(.79f, .88f, .81f), 29f);
                }
                CitySurfaceLibrary.ApplyHierarchy(root);
            }
        }

        private static GameObject Template(string name) => Resources.Load<GameObject>(ResourceFolder + name)
            ?? throw new InvalidOperationException("Missing municipal stair fitting " + name);

        private static GameObject Fitting(Transform parent, string name, Vector3 at, Quaternion rotation) =>
            Model(Template(name), parent, name, at, rotation, Vector3.one);

        // Keep the standard on the landing edge. Light crosses the nosings diagonally
        // so the leading edges stay visible from either approach.
        private static void LightFlight(Transform parent, CityExteriorStairFlightDescriptor flight, Color color, float intensity)
        {
            float side = Mathf.Max(CityHillsideShortcut.Width, flight.Width) * .5f - .11f;
            Vector3 foot = flight.End + flight.Direction * .17f + flight.Right * side;
            StairLamp(parent, foot, -flight.Right, Vector3.Lerp(flight.Start, flight.End, .38f), color, intensity);
        }

        private static void StairLamp(Transform parent, Vector3 foot, Vector3 inward, Vector3 target, Color color, float strength)
        {
            var post = Fitting(parent, "StairLampPost", foot, Quaternion.LookRotation(inward)).transform;
            var head = CityCoveredPassageAssetProvider.Create("PassageLamp", post, new Vector3(0, 2.646f, .44f),
                Quaternion.identity, CityResidentialEntranceLighting.ModelScale);
            foreach (var lens in head.GetComponentsInChildren<MeshRenderer>())
                if (lens.name.EndsWith("__Lens", StringComparison.Ordinal))
                {
                    lens.shadowCastingMode = ShadowCastingMode.Off;
                    CityNightGlowRegistry.Register(lens, color * 2.15f);
                }
            var source = CityResidentialCourtyardAssetProvider.FindPart(head, "ANCHOR_LightSource");
            var light = new GameObject("Stair landing light").AddComponent<Light>();
            light.transform.SetParent(post, false);
            light.transform.position = source.position + Vector3.down * .08f;
            light.transform.rotation = Quaternion.LookRotation(parent.TransformPoint(target) - light.transform.position);
            light.type = LightType.Spot;
            light.color = color;
            light.range = 12f;
            light.spotAngle = 132f;
            light.innerSpotAngle = 75f;
            light.shadows = LightShadows.Soft;
            light.shadowNearPlane = .07f;
            light.shadowBias = .025f;
            light.shadowNormalBias = .09f;
            var shadow = light.GetUniversalAdditionalLightData();
            shadow.usePipelineSettings = false;
            if (Application.isPlaying)
                shadow.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
            var halo = CityLightHalo.CreateAlwaysBurning(light.transform, Vector3.zero, .10f, .34f,
                new Color(color.r, color.g, color.b, .14f), new Color(color.r, color.g, color.b, .035f));
            CityNightSiteLightRegistry.Register(light, strength, halo);
        }

        private static void DressLowerCourt(Transform parent, CityTerraceDistrictPlan plan)
        {
            foreach (var flight in plan.Flights)
            {
                if (flight.Id == "link-court-flight-1" || flight.Id == "link-court-flight-3" || flight.Id == "link-court-flight-4")
                    LightFlight(parent, flight, new Color(1f, .85f, .65f), 32f);
                if (flight.Id == "link-court-flight-4")
                {
                    Vector3 tread = flight.Start + flight.Direction * (3 * flight.TreadDepth) + Vector3.up * (4 * flight.StepRise);
                    Fitting(parent, "CourtRepairTread", tread, Quaternion.LookRotation(flight.Direction));
                }
            }
            foreach (var floor in plan.Floors)
            {
                if (floor.Id == "link-inner-turn")
                    Fitting(parent, "CourtPourJoint", floor.Center + Vector3.right * (floor.Size.x * .5f - .08f)
                        + Vector3.forward * 1.05f, Quaternion.Euler(0f, 180f, 0f));
                if (floor.Id == "link-bottom-turn")
                {
                    var face = Fitting(parent, "CourtShoeWear", floor.Center, Quaternion.identity);
                    var renderer = face.GetComponentInChildren<Renderer>();
                    Texture2D texture = Resources.Load<Texture2D>(ResourceFolder + "CourtShoeTurn");
                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    block.SetTexture("_BaseMap", texture);
                    block.SetVector("_BaseMap_ST", new Vector4(2f / 1.78f, 2f / 1.72f, .5f, .5f));
                    renderer.SetPropertyBlock(block);
                }
            }
        }

        private static void DressGable(Transform root, CityLayout layout, CityHillsideShortcut stair)
        {
            Color light = new(.83f, .93f, .85f);
            LightFlight(root, stair.Flights[0], light, 36f);
            LightFlight(root, stair.Flights[2], light, 32f);
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(layout).Buildings)
                if (house.Lot.Cell == new Vector2Int(22, -5))
                {
                    var at = new Vector3(house.FoundationBounds.xMin - .18f, house.ThresholdWorld.y,
                        house.FoundationBounds.yMin + .55f);
                    var drain = Fitting(root, "GableRainShoe", at, Quaternion.identity);
                    // Only the straight downpipe stretches from its socket to the eaves.
                    foreach (var part in drain.GetComponentsInChildren<Renderer>())
                        if (part.name.StartsWith("Painted downpipe", StringComparison.Ordinal))
                        {
                            float height = house.Lot.Height;
                            var t = part.transform;
                            Vector3 socket = new(at.x, at.y + .59f, at.z);
                            Vector3 pivot = t.InverseTransformPoint(root.TransformPoint(socket));
                            Vector3 before = t.TransformPoint(pivot);
                            Vector3 localUp = t.InverseTransformDirection(root.up);
                            Vector3 scale = t.localScale;
                            int axis = Mathf.Abs(localUp.y) > Mathf.Abs(localUp.z) ? 1 : 2;
                            scale[axis] *= (height - .59f) / (3.4f - .59f);
                            t.localScale = scale;
                            t.position += before - t.TransformPoint(pivot);
                        }
                    break;
                }
        }
    }
}
