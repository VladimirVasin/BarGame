using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Passive imported geometry with metre-based collision. No visible runtime primitives.</summary>
    public static class CityTerracePilotWorld
    {
        public const string ResourceFolder = "CityTerraces/";

        public static GameObject Build(Transform parent, CityTerracePilotPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            return BuildParts(parent, plan.Floors, plan.Flights, plan, null);
        }

        public static GameObject Build(Transform parent, CityTerraceDistrictPlan district)
            => district.IsEnabled ? BuildParts(parent, district.Floors, district.Flights, null, district) : null;

        private static GameObject BuildParts(Transform parent,
            IReadOnlyList<CityTerracePilotFloor> floors,
            IReadOnlyList<CityExteriorStairFlightDescriptor> flights,
            CityTerracePilotPlan pilot, CityTerraceDistrictPlan district)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            var root = new GameObject("Terraced Courtyards");
            root.transform.SetParent(parent, false);
            if (district != null)
            {
                root.transform.localPosition = district.Origin;
                root.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            float foundationBottom = district == null ? -.30f : -3f;
            FootProbeSurface.EnsureRuntimePolicy();
            FootstepGround.Stamp(root, FootstepGroundKind.Stone);
            // Only this finite build owns references. The resource assets and
            // City's material cache are shared, never destroyed per instance.
            var templates = new Dictionary<string, GameObject>();
            foreach (string name in new[] { "TerraceFlight", "TerraceSupport", "TerraceRail" }) LoadModel(name);
            foreach (CityTerracePilotFloor floor in floors) LoadModel(floor.ModelName);
            if (district != null) LoadModel("TerracePassage");

            void LoadModel(string name)
            {
                if (templates.ContainsKey(name)) return;
                GameObject model = Resources.Load<GameObject>(ResourceFolder + name);
                if (model == null) throw new InvalidOperationException("Missing terrace model: " + name);
                templates.Add(name, model);
            }
            foreach (CityTerracePilotFloor floor in floors)
            {
                float thickness = floor.Thickness;
                Model(templates[floor.ModelName], root.transform,
                    floor.Id, floor.Center, Quaternion.identity, Vector3.one);
                Box(root.transform, floor.Id + " ground", floor.Center - Vector3.up * thickness * .5f,
                    new Vector3(floor.Size.x, thickness, floor.Size.y), Quaternion.identity);
                if (floor.HasFoundation)
                    Support(templates["TerraceSupport"], root.transform, floor.Id + " support",
                        floor.Center - Vector3.up * thickness, floor.Size, foundationBottom);
                if (district == null || !floor.Id.StartsWith("residence-entry-", StringComparison.Ordinal))
                    BuildFloorRails(root.transform, templates["TerraceRail"], floors, flights, floor, district);
            }
            foreach (CityExteriorStairFlightDescriptor flight in flights)
            {
                Quaternion yaw = Quaternion.LookRotation(flight.Direction);
                Model(templates["TerraceFlight"], root.transform, flight.Id, flight.Start, yaw, Vector3.one);
                Vector3 delta = flight.End - flight.Start;
                Quaternion slope = Quaternion.LookRotation(delta.normalized, Vector3.up);
                Box(root.transform, flight.Id + " continuous ramp",
                    (flight.Start + flight.End) * .5f - slope * Vector3.up * .06f,
                    new Vector3(flight.Width, .12f, delta.magnitude), slope);
                Vector3 supportTop = flight.Start + flight.Direction * (flight.RunLength * .5f) - Vector3.up * .25f;
                Vector2 supportSize = Mathf.Abs(flight.Direction.z) > .5f
                    ? new Vector2(2.1f, flight.RunLength) : new Vector2(flight.RunLength, 2.1f);
                Support(templates["TerraceSupport"], root.transform, flight.Id + " support", supportTop, supportSize, foundationBottom);
                for (int side = -1; side <= 1; side += 2)
                    Box(root.transform, flight.Id + " rail " + side,
                        (flight.Start + flight.End) * .5f + Vector3.up * .55f + flight.Right * (side * .99f),
                        new Vector3(.12f, 1.05f, delta.magnitude), slope);
                for (int step = 0; step < flight.StepCount; step++)
                {
                    Vector3 top = flight.Start + flight.Direction * ((step + .5f) * flight.TreadDepth) +
                        Vector3.up * ((step + 1) * flight.StepRise);
                    BoxCollider probe = Box(root.transform, flight.Id + " tread " + step,
                        top - Vector3.up * .025f, new Vector3(flight.Width, .05f, flight.TreadDepth), yaw);
                    probe.gameObject.layer = FootProbeSurface.LayerIndex;
                    probe.isTrigger = true;
                }
            }
            if (pilot != null) BuildResidentialContext(root.transform, templates["TerraceSupport"]);
            if (district != null)
            {
                // The plain imported paving slab follows the measured street
                // attachment. Stair and rail modules retain their authored metres.
                Vector3 start = district.Local(district.EntryStartWorld);
                Vector3 end = district.Local(district.EntryEndWorld);
                Vector3 delta = end - start;
                Quaternion slope = Quaternion.LookRotation(delta.normalized, Vector3.up);
                Vector3 center = (start + end) * .5f;
                Model(templates["TerraceCourt"], root.transform, "Terrace street approach", center,
                    slope, new Vector3(district.EntryBounds.height / 10f, 1f, delta.magnitude / 10f));
                Box(root.transform, "Terrace street approach collision", center - slope * Vector3.up * .15f,
                    new Vector3(district.EntryBounds.height, .30f, delta.magnitude), slope);

                start = district.Local(district.LowerPassStartWorld);
                end = district.Local(district.LowerPassEndWorld);
                delta = end - start;
                slope = Quaternion.LookRotation(delta.normalized, Vector3.up);
                center = (start + end) * .5f;
                Model(templates["TerracePassage"], root.transform, "Terrace lower passage", center,
                    slope, new Vector3(1f, 1f, delta.magnitude / 21.5f));
                Box(root.transform, "Terrace lower passage collision", center - slope * Vector3.up * .15f,
                    new Vector3(CityTerraceDistrictPlan.LowerPassWidth, .30f, delta.magnitude), slope);

                // The bounded trigger clears particles emitted beneath the
                // deck as well as those falling from above. Its ceiling stops
                // below the upper walking surface, which stays exposed to rain.
                float ceiling = district.Pilot.TotalRise - .35f;
                float bottom = district.Local(district.LowerCrossingWorld).y - .30f;
                BoxCollider shelter = Box(root.transform, "Terrace bridge rain shelter",
                    new Vector3(0f, (bottom + ceiling) * .5f, 20.08f),
                    new Vector3(4.8f, ceiling - bottom, 2.4f), Quaternion.identity);
                shelter.gameObject.layer = 2; // IgnoreRaycast: atmosphere volume, never player geometry.
                shelter.isTrigger = true;
                root.AddComponent<CityTerraceWorldAnchors>().Initialize(shelter);
            }
            CitySurfaceLibrary.ApplyHierarchy(root.transform);
            Physics.SyncTransforms();
            return root;
        }

        private static void BuildResidentialContext(Transform root, GameObject support)
        {
            // Reuse actual authored district prototypes and their appearance;
            // there is no new facade grammar or local NPC population here.
            CityLayout source = CityLayoutCache.GetOrGenerate(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            CityBuildingAssetProvider provider = CityBuildingAssetProvider.LoadOrThrow();
            int placed = 0;
            foreach (BuildingLot lot in source.BuildingLots)
            {
                if (!lot.IsOrdinaryBuilding || lot.District != CityDistrictKind.Residential) continue;
                GameObject prefab = provider.GetPrefabOrThrow(lot.District, lot.BuildingVariant);
                GameObject model = UnityEngine.Object.Instantiate(prefab, root, false);
                model.name = "Pilot residential context " + placed;
                CityBuildingAssetRegistry registry = model.GetComponent<CityBuildingAssetRegistry>();
                Bounds bounds = registry.LocalBounds;
                float y = placed == 0 ? 0f : placed == 1 ? 3.84f : 7.68f;
                Vector3 center = placed == 0 ? new Vector3(-19.3f - bounds.extents.x, y, 10.04f) :
                    placed == 1 ? new Vector3(19.3f + bounds.extents.x, y, 10.04f) :
                    new Vector3(0f, y, 27f + bounds.extents.z);
                model.transform.localPosition = center - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                CityBuildingPrototypeWorldBuilder.ApplyAppearance(registry, lot, source.Seed);
                Support(support, root, model.name + " foundation", center, new Vector2(bounds.size.x, bounds.size.z));
                Box(root, model.name + " collision", center + Vector3.up * bounds.extents.y,
                    bounds.size, Quaternion.identity);
                if (++placed == 3) break;
            }
            if (placed != 3) throw new InvalidOperationException("The terrace pilot needs three existing residential prototypes.");
        }

        private static void Support(GameObject template, Transform root, string name, Vector3 top, Vector2 size,
            float bottom = -.30f)
        {
            float depth = top.y - bottom;
            if (depth <= .051f) return;
            Model(template, root, name, top, Quaternion.identity,
                new Vector3(size.x / 2.4f, depth / 1.92f, size.y / 2.4f));
            Box(root, name + " collision", top - Vector3.up * depth * .5f,
                new Vector3(size.x, depth, size.y), Quaternion.identity);
        }

        private static GameObject Model(GameObject template, Transform parent, string name,
            Vector3 position, Quaternion rotation, Vector3 scale)
        {
            // A unit wrapper owns the placed metres; the FBX's imported unit
            // factor stays on its original root, including for scaled supports.
            var wrapper = new GameObject(name);
            wrapper.transform.SetParent(parent, false);
            wrapper.transform.localPosition = position;
            wrapper.transform.localRotation = rotation;
            wrapper.transform.localScale = scale;
            GameObject model = UnityEngine.Object.Instantiate(template, wrapper.transform, false);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                int separator = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                string role = separator >= 0 ? renderer.name.Substring(separator + 2) : "Concrete";
                // Blender numbers repeated mesh names across kit modules.
                int suffix = role.IndexOf('.');
                if (suffix >= 0) role = role.Substring(0, suffix);
                renderer.sharedMaterial = CityPortAssetProvider.GetSurfaceMaterial(role);
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                if (role == "Concrete" || role == "ConcreteWall")
                    GroundAuthoredSurfaceAppearance.Apply(renderer, GroundSurfaceKind.Concrete, cityClimate: true);
            }
            return wrapper;
        }

        private static BoxCollider Box(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation)
        {
            var body = new GameObject(name);
            body.transform.SetParent(parent, false);
            body.transform.localPosition = center;
            body.transform.localRotation = rotation;
            BoxCollider collider = body.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        private static void BuildFloorRails(Transform root, GameObject template,
            IReadOnlyList<CityTerracePilotFloor> floors, IReadOnlyList<CityExteriorStairFlightDescriptor> flights,
            CityTerracePilotFloor floor, CityTerraceDistrictPlan district)
        {
            for (int axis = 0; axis < 2; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                bool alongZ = axis == 0;
                Vector3 normal = (alongZ ? Vector3.right : Vector3.forward) * sign;
                Vector3 tangent = alongZ ? Vector3.forward : Vector3.right;
                float halfNormal = (alongZ ? floor.Size.x : floor.Size.y) * .5f;
                float halfRun = (alongZ ? floor.Size.y : floor.Size.x) * .5f;
                Vector3 center = floor.Center + normal * halfNormal;
                var gaps = new List<Vector2>();
                foreach (CityExteriorStairFlightDescriptor flight in flights)
                {
                    void AddEnd(Vector3 point)
                    {
                        Vector3 relative = point - center;
                        if (Mathf.Abs(relative.y) > .03f || Mathf.Abs(Vector3.Dot(relative, normal)) > .10f ||
                            Mathf.Abs(Vector3.Dot(flight.Direction, normal)) < .99f) return;
                        float at = Vector3.Dot(relative, tangent);
                        gaps.Add(new Vector2(at - 1.07f, at + 1.07f));
                    }
                    AddEnd(flight.Start);
                    AddEnd(flight.End);
                }
                foreach (CityTerracePilotFloor other in floors)
                {
                    if (other.Id == floor.Id || Mathf.Abs(other.Center.y - center.y) > .01f) continue;
                    float otherHalf = (alongZ ? other.Size.x : other.Size.y) * .5f;
                    Vector3 relative = other.Center - center;
                    float distance = Vector3.Dot(relative, normal);
                    if (distance + otherHalf < .05f || distance - otherHalf > .10f) continue;
                    float at = Vector3.Dot(relative, tangent);
                    float half = (alongZ ? other.Size.y : other.Size.x) * .5f;
                    gaps.Add(new Vector2(at - half, at + half));
                }
                if (district != null)
                {
                    AddOpening(district.Local(district.EntryEndWorld), district.EntryBounds.height);
                    AddOpening(district.Local(district.LowerPassStartWorld), CityTerraceDistrictPlan.LowerPassWidth);
                    foreach (Vector3 door in district.HouseEntryWorld) AddOpening(district.Local(door), 1.8f);
                }
                gaps.Sort((a, b) => a.x.CompareTo(b.x));
                float cursor = -halfRun;
                foreach (Vector2 gap in gaps)
                {
                    AddRail(cursor, Mathf.Clamp(gap.x, -halfRun, halfRun));
                    cursor = Mathf.Max(cursor, Mathf.Clamp(gap.y, -halfRun, halfRun));
                }
                AddRail(cursor, halfRun);

                void AddOpening(Vector3 point, float width)
                {
                    Vector3 relative = point - center;
                    if (Mathf.Abs(relative.y) > .04f || Mathf.Abs(Vector3.Dot(relative, normal)) > .10f) return;
                    float at = Vector3.Dot(relative, tangent);
                    gaps.Add(new Vector2(at - width * .5f, at + width * .5f));
                }

                void AddRail(float from, float to)
                {
                    float length = to - from;
                    if (length < .10f) return;
                    // Split long edges into unchanged post-spacing modules;
                    // only the short end segment is trimmed to its metre span.
                    int count = Mathf.CeilToInt(length / 2.4f);
                    for (int i = 0; i < count; i++)
                    {
                        float start = from + i * 2.4f;
                        float run = Mathf.Min(2.4f, to - start);
                        Vector3 position = center + tangent * start - normal * .06f;
                        Quaternion yaw = Quaternion.LookRotation(tangent);
                        Model(template, root, floor.Id + " edge rail", position, yaw, new Vector3(1f, 1f, run / 2.4f));
                        Box(root, floor.Id + " edge guard", position + tangent * run * .5f + Vector3.up * .525f,
                            new Vector3(.12f, 1.05f, run), yaw);
                    }
                }
            }
        }
    }
}
