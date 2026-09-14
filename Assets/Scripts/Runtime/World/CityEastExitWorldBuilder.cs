using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public sealed class CityEastExitWorldResult
    {
        internal CityEastExitWorldResult(GameObject root, CityFringePracticalAnchor practical)
        { Root = root; Practical = practical; }
        public GameObject Root { get; }
        public CityFringePracticalAnchor Practical { get; }
    }

    /// <summary>Imported passive geometry; the plan owns all placement and collision.</summary>
    public static class CityEastExitWorldBuilder
    {
        public const string ResourcePath = "City/EastExit/CityEastExit3D";
        public const string RootName = "Eastern Mainland Exit";
        public const string CanopyLightName = "Checkpoint Canopy Work Light";
        public const string ServiceLightName = "Checkpoint Service Shed Light";
        public const float CanopyNightIntensity = 9.5f;
        public const float ServiceNightIntensity = 10.5f;

        public static CityEastExitWorldResult Build(Transform parent, CityEastExitPlan plan,
            CityFringeYardPlan fringePlan)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.IsEnabled) return null;
            if (fringePlan == null) throw new ArgumentNullException(nameof(fringePlan));
            GameObject asset = Resources.Load<GameObject>(ResourcePath);
            if (asset == null) throw new InvalidOperationException("Missing authored eastern exit kit: " + ResourcePath);
            var templates = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform candidate in asset.GetComponentsInChildren<Transform>(true))
                if (!templates.ContainsKey(candidate.name)) templates.Add(candidate.name, candidate);
            CityEastExitDressingWorldBuilder.AddTemplates(templates);
            CityEastExitDressingPlan dressing = CityEastExitDressingPlan.Create(plan);
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);

            var stops = new List<float> { plan.ApproachStart.x,
                plan.ApproachStart.x + CityEastExitPlan.StreetGradeBlendLength,
                plan.CheckpointPosition.x - CityEastExitPlan.CheckpointApronLength,
                plan.CheckpointPosition.x, plan.RoadEnd.x };
            int ordinal = 0;
            for (int s = 1; s < stops.Count; s++)
            {
                float first = stops[s - 1], last = stops[s];
                int count = Mathf.CeilToInt((last - first) / 4f);
                for (int i = 0; i < count; i++)
                {
                    float ax = Mathf.Lerp(first, last, i / (float)count);
                    float bx = Mathf.Lerp(first, last, (i + 1f) / count);
                    Vector3 a = new Vector3(ax, plan.SampleRoadTop(ax) - CityEastExitPlan.RoadSurfaceLift, plan.CheckpointPosition.z);
                    Vector3 b = new Vector3(bx, plan.SampleRoadTop(bx) - CityEastExitPlan.RoadSurfaceLift, plan.CheckpointPosition.z);
                    Transform road = Place(templates, root, "Road", "Road " + ordinal++, (a + b) * .5f,
                        Quaternion.identity, new Vector3((bx - ax) / 10f, 1f, 1f));
                    FitRoad(road, plan);
                    FootstepGround.Stamp(road.gameObject, FootstepGroundKind.Concrete);
                }
            }
            // One cooked mesh welds the shared slab edges. Separate collider
            // cooks can reject a ray exactly on the boundary of both slabs.
            var roadCollision = new List<CombineInstance>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name.EndsWith("_Asphalt", StringComparison.Ordinal) ||
                    filter.name.EndsWith("_Ground", StringComparison.Ordinal))
                    roadCollision.Add(new CombineInstance { mesh = filter.sharedMesh,
                        transform = root.worldToLocalMatrix * filter.transform.localToWorldMatrix });
            var collisionMesh = new Mesh { name = "Continuous East Road Collision" };
            collisionMesh.CombineMeshes(roadCollision.ToArray(), true, true);
            Transform collisionRoot = new GameObject("EEX_Road_Collision").transform;
            collisionRoot.SetParent(root, false);
            collisionRoot.gameObject.AddComponent<MeshCollider>().sharedMesh = collisionMesh;
            collisionRoot.gameObject.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(collisionMesh);
            FootstepGround.Stamp(collisionRoot.gameObject, FootstepGroundKind.Concrete);
            Transform booth = Place(templates, root, "Booth", "Civilian Booth", plan.BoothPosition, Quaternion.identity, Vector3.one);
            AddBox(booth, new Vector3(0f, 1.5475f, 0f), new Vector3(3f, 3.095f, 3.4f));
            Transform barrier = Place(templates, root, "Barrier", "Closed Road Barrier",
                plan.CheckpointPosition - Vector3.up * CityEastExitPlan.RoadSurfaceLift, Quaternion.identity, Vector3.one);
            AddBox(barrier, new Vector3(0f, .65f, 0f), new Vector3(.42f, 1.30f, 6f));
            Vector3 gatePosition = plan.CheckpointPosition + Vector3.back * 3.7f;
            gatePosition.y = plan.SampleGroundTop(new Vector2(gatePosition.x, gatePosition.z));
            Transform gate = Place(templates, root, "Gate", "Closed Pedestrian Gate", gatePosition,
                Quaternion.identity, Vector3.one);
            AddBox(gate, new Vector3(0f, .9275f, 0f), new Vector3(.20f, 1.855f, 1.4f));
            ordinal = 0;
            foreach (CityEastExitFence span in plan.Fences)
            {
                Vector3 delta = span.End - span.Start;
                bool repaired = dressing.IsRepairSpan(span);
                Transform fence = Place(templates, root, repaired ? "RepairedFence" : "Fence",
                    (repaired ? "Repaired Boundary Fence " : "Boundary Fence ") + ordinal++,
                    (span.Start + span.End) * .5f,
                    Quaternion.LookRotation(delta.normalized, Vector3.up), new Vector3(1f, 1f, delta.magnitude / 4f));
                AddBox(fence, new Vector3(0f, .9275f, 0f), new Vector3(CityEastExitPlan.FenceThickness, 1.855f, 4f));
            }
            float drainX = plan.YardBounds.xMin + 10.5f;
            Place(templates, root, "DrainCover", "Road Drain Crossing",
                new Vector3(drainX, plan.SampleRoadTop(drainX) + .015f, plan.CheckpointPosition.z),
                Quaternion.identity, Vector3.one);

            CityEastExitDressingWorldBuilder.Build(root, plan, dressing, templates);
            BuildSiteLights(root, dressing, fringePlan, templates);
            Transform lamp = Place(templates, root, "Lamp", "Checkpoint Practical", plan.LampPosition,
                Quaternion.identity, Vector3.one);
            Transform lightAnchor = null;
            foreach (Transform part in lamp.GetComponentsInChildren<Transform>())
                if (part.name == "LightAnchor") { lightAnchor = part; break; }
            if (lightAnchor == null) throw new InvalidOperationException("Checkpoint lamp lacks its measured LightAnchor.");
            lightAnchor.rotation = Quaternion.LookRotation(new Vector3(.32f, -1f, .35f), Vector3.up);
            // This is the fifth eligible location for the existing last
            // street Spot; it never creates another realtime Light.
            return new CityEastExitWorldResult(root.gameObject,
                new CityFringePracticalAnchor(CityFringeYardKind.EastUtilityEdge, lightAnchor));
        }

        private static void BuildSiteLights(Transform root, CityEastExitDressingPlan dressing,
            CityFringeYardPlan fringePlan, IDictionary<string, Transform> templates)
        {
            bool canopyFound = false, shedFound = false;
            foreach (CityEastExitDressingPart part in dressing.Parts)
            {
                if (part.Id != "Booth Shelter") continue;
                // The mounting plate touches the existing roof underside.
                // Use the actual shelter plan, including its level support pad.
                Transform fixture = Place(templates, root, "CanopyLamp", "Checkpoint Canopy Fixture",
                    part.Position + part.Rotation * new Vector3(0f, 2.44f, .15f), part.Rotation, Vector3.one);
                AddSiteLight(fixture, CanopyLightName, Vector3.down, CanopyNightIntensity, 5.2f, 120f, 88f);
                canopyFound = true;
                break;
            }
            foreach (CityFringeYardDescriptor yard in fringePlan.Yards)
            {
                if (yard.Kind != CityFringeYardKind.EastUtilityEdge) continue;
                foreach (CityFringeYardPartDescriptor shed in yard.Parts)
                {
                    if (shed.StableId != "yard-east-utility-shed-00") continue;
                    // The shed shell has its own terrain embed. Its imported
                    // root, rather than an independent ground sample, owns
                    // the height of this ordinary door fixture.
                    Vector3 modelRoot = shed.Center + Vector3.up * (-shed.Size.y * .5f + .16f);
                    Transform fixture = Place(templates, root, "ServiceWallLamp", "Checkpoint Service Shed Fixture",
                        modelRoot + shed.Rotation * new Vector3(-shed.Size.x * .5f - .005f, 2.55f, 0f),
                        shed.Rotation * Quaternion.Euler(0f, 90f, 0f), Vector3.one);
                    AddSiteLight(fixture, ServiceLightName, shed.Rotation * new Vector3(-.65f, -1f, 0f),
                        ServiceNightIntensity, 6.5f, 105f, 65f);
                    shedFound = true;
                    break;
                }
            }
            if (!canopyFound || !shedFound)
                throw new InvalidOperationException("Checkpoint local lamps require their existing canopy and first service shed.");
        }

        private static void AddSiteLight(Transform fixture, string name, Vector3 direction,
            float intensity, float range, float angle, float innerAngle)
        {
            Transform anchor = null;
            foreach (Transform candidate in fixture.GetComponentsInChildren<Transform>(true))
                if (candidate.name.EndsWith("LightAnchor", StringComparison.Ordinal)) { anchor = candidate; break; }
            if (anchor == null) throw new InvalidOperationException(name + " lacks its authored lens anchor.");
            // Preserve the imported anchor's world metres. The emitter itself
            // stays outside the FBX unit-scaled hierarchy so its halo does too.
            Transform emitter = new GameObject(name).transform;
            emitter.SetParent(fixture, false);
            emitter.position = anchor.position;
            emitter.rotation = Quaternion.LookRotation(direction, Vector3.forward);
            Light light = emitter.gameObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1f, .72f, .42f);
            light.range = range;
            light.spotAngle = angle;
            light.innerSpotAngle = innerAngle;
            light.shadows = LightShadows.Hard;
            light.shadowStrength = .78f;
            light.shadowBias = .015f;
            light.shadowNormalBias = .06f;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            CityLightHalo halo = CityLightHalo.CreateAlwaysBurning(emitter, Vector3.zero,
                .16f, .52f, new Color(1f, .72f, .42f, .12f), new Color(.8f, .51f, .3f, .035f));
            // These two accepted fixed site lamps follow the shared day floor
            // and scene lifetime; the original twelve-slot pool is unchanged.
            CityNightSiteLightRegistry.Register(light, intensity, halo);
        }

        private static void FitRoad(Transform road, CityEastExitPlan plan)
        {
            // Deform the imported slab onto the same grade used by terrain
            // and landings. Tilting one box cannot match the crossfall at a
            // T-junction, and overlapping the city slab hides the seam badly.
            foreach (MeshFilter filter in road.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = Object.Instantiate(filter.sharedMesh);
                mesh.name = "Fitted " + filter.sharedMesh.name;
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 world = filter.transform.TransformPoint(vertices[i]);
                    // Consecutive slabs share a full top edge. Keeping the
                    // kit's end chamfers leaves a narrow V-shaped hole at
                    // every join once the duplicate terrain is removed.
                    Vector3 local = road.InverseTransformPoint(world);
                    if (Mathf.Abs(local.x) > 4.98f)
                    {
                        local.x = Mathf.Sign(local.x) * 5f;
                        world = road.TransformPoint(local);
                    }
                    float offset = world.y - road.position.y;
                    world.y = plan.SampleRoadTop(world.x, world.z) - CityEastExitPlan.RoadSurfaceLift + offset;
                    vertices[i] = filter.transform.InverseTransformPoint(world);
                }
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                filter.sharedMesh = mesh;
                filter.gameObject.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            }
        }

        internal static Transform Place(IDictionary<string, Transform> templates, Transform parent,
            string assembly, string name, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (!templates.TryGetValue(assembly, out Transform template))
                throw new InvalidOperationException("Eastern model lacks assembly " + assembly);
            Transform placement = new GameObject(name).transform;
            placement.SetParent(parent, false);
            placement.SetPositionAndRotation(position, rotation);
            placement.localScale = scale;
            Transform clone = Object.Instantiate(template, placement, false);
            clone.localPosition = Vector3.zero;
            // FBX's bake-space correction can live on an ancestor empty.
            // Detaching a child must retain the complete imported basis,
            // exactly as it retains the root's hundredfold unit scale.
            clone.localRotation = template.rotation;
            clone.localScale = template.lossyScale;
            if (assembly == "Road" || assembly == "Booth") ValidatePlacedAssembly(placement, assembly);
            foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = assembly == "Booth" || assembly == "Lamp" || assembly == "Shelter" ||
                    assembly == "Bench" || assembly == "UtilityCabinet"
                    ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                string[] words = renderer.name.Split('_');
                string role = words.Length >= 3 ? words[2] : "Metal";
                ApplyAppearance(renderer, role);
            }
            return placement;
        }

        private static void ValidatePlacedAssembly(Transform placement, string assembly)
        {
            Bounds bounds = default;
            bool first = true;
            foreach (MeshFilter filter in placement.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds mesh = filter.sharedMesh.bounds;
                Matrix4x4 matrix = placement.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(mesh.center + Vector3.Scale(mesh.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f,
                            (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f)));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            Vector3 expected = assembly == "Road" ? new Vector3(10f, .238f, 8f) :
                new Vector3(3.46f, 3.094839f, 4.10f);
            if (first || (bounds.size - expected).sqrMagnitude > .012f)
                throw new InvalidOperationException("Eastern imported " + assembly +
                    " changed its placed metre bounds: " + bounds + "; expected size " + expected);
        }

        internal static void AddBox(Transform parent, Vector3 center, Vector3 size)
        {
            BoxCollider collider = parent.gameObject.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
        }

        private static void ApplyAppearance(Renderer renderer, string role)
        {
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            Color tint;
            switch (role)
            {
                case "Asphalt": CityExteriorAppearance.ApplyRoadSurface(renderer); return;
                case "Ground": CityExteriorAppearance.ApplyGroundSurface(renderer, new Color(.27f, .28f, .22f)); return;
                case "Gravel": CityFringeYardSurfaceAppearance.ApplyCombined(renderer, CityFringeYardSurfaceKind.ForefieldGround,
                    new Color(.33f, .34f, .28f)); return;
                case "DryGrass": tint = new Color(.35f, .34f, .23f); break;
                case "Wall": case "Stone":
                    CityFringeYardSurfaceAppearance.ApplyCombined(renderer, CityFringeYardSurfaceKind.Concrete,
                        new Color(.38f, .40f, .34f)); return;
                case "Roof": tint = new Color(.20f, .25f, .23f); break;
                case "Glass": tint = new Color(.17f, .23f, .21f); break;
                case "Paint": tint = new Color(.60f, .55f, .39f); break;
                case "Wood": tint = new Color(.30f, .25f, .18f); break;
                case "Foliage": tint = new Color(.19f, .25f, .16f); break;
                case "Dark": tint = new Color(.055f, .065f, .06f); break;
                case "Glow":
                    renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
                    CityNightGlowRegistry.Register(renderer, new Color(2.5f, 1.5f, .65f)); return;
                default: tint = new Color(.20f, .24f, .22f); break;
            }
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", tint);
            properties.SetColor("_Color", tint);
            renderer.SetPropertyBlock(properties);
        }
    }
}
