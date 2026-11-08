using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Fixed-metre domestic edges on the two banks and their shared court.
    /// City and the apartment sample the same ground and keep the same free approaches.</summary>
    internal static class CityHillsideSiteDetails
    {
        internal enum Kind { RetainingEnd, CourtEdge, BearingTalus, PlinthRepair, GardenVerge, YardTree, YardShelter }

        internal readonly struct Site
        {
            internal readonly Kind Part;
            internal readonly Vector3 Origin;
            internal readonly Quaternion Rotation;
            internal readonly Rect Bounds;
            internal readonly int Variant;
            internal readonly float Scale;

            internal Site(Kind part, Vector3 origin, Quaternion rotation, int variant = 0, float scale = 1f)
            {
                Part = part;
                Origin = origin;
                Rotation = rotation;
                Variant = variant;
                Scale = scale;
                // The retained soil tapers 2.65 m behind the face, the
                // drain projects only 0.57 m in front.
                var half = part switch
                {
                    Kind.RetainingEnd => new Vector2(3f, 1.61f),
                    Kind.CourtEdge => new Vector2(1.90f, .38f),
                    Kind.BearingTalus => new Vector2(1.5f, .75f),
                    Kind.GardenVerge => new Vector2(4.9f, 1.45f),
                    Kind.YardTree => Vector2.one * (1.75f * scale),
                    Kind.YardShelter => new Vector2(1.25f, .625f),
                    _ => new Vector2(1.03f, .04f)
                };
                var centre = part == Kind.RetainingEnd ? origin - rotation * Vector3.forward * 1.04f : origin;
                var right = rotation * Vector3.right * half.x;
                var front = rotation * Vector3.forward * half.y;
                float dx = Mathf.Abs(right.x) + Mathf.Abs(front.x);
                float dz = Mathf.Abs(right.z) + Mathf.Abs(front.z);
                Bounds = Rect.MinMaxRect(centre.x - dx, centre.z - dz, centre.x + dx, centre.z + dz);
            }
        }

        private static readonly ConditionalWeakTable<CityLayout, List<Site>> Plans = new();
        private static readonly string[] Names = { "RidgeRetainingEnd", "RidgeCourtEdge", "RidgeBearingTalus",
            "RidgePlinthRepair", "RidgeGardenVerge", "RidgeYardTree", "RidgeYardShelter" };
        internal static IReadOnlyList<Site> ForLayout(CityLayout layout) => Plans.GetValue(layout, Plan);

        private static List<Site> Plan(CityLayout layout)
        {
            var sites = new List<Site>();
            var quarter = CityHillsideQuarterPlan.ForLayout(layout);
            if (quarter.Links.Count == 0) return sites;
            var housing = CityHillsideNeighborhoodPlan.ForLayout(layout);
            var stairs = CityHillsideAccessPlan.ForLayout(layout);
            var banks = CityHillsideGroundGrid.ForLayout(layout);
            var reserved = new List<Rect>();
            if (layout.HasSummitHome) reserved.Add(CityGlassReturnSitePlan.LandReservation);
            foreach (var house in housing.AllBuildings)
            {
                reserved.Add(house.FoundationBounds);
                reserved.Add(house.CourtBounds);
                reserved.Add(house.ApproachBounds);
            }
            foreach (var stair in stairs.AllStairs) reserved.AddRange(stair.WalkableBounds);
            CityBusStopDescriptor crestStop = null;
            foreach (var stop in CityBusNetworkFurniture.Stops(CityDistrictBusPlanner.CreateRoadRouting(layout)))
            {
                if (stop.RoadEdge.A != stop.RoadEdge.B) continue;
                if (stop.TargetKind == CityBusStopTargetKind.PlayerHome) crestStop = stop;
                reserved.Add(CityBusStopWorldBuilder.DescribeFurnitureFootprint(stop));
                reserved.Add(CityBusPlatformClearance.PassengerAccess(stop.Position, stop.Forward,
                    stop.ShelterPosition, stop.RoadsideForward));
            }

            // Long west sections face the fall in the ground. East courts turn
            // their shorter domestic edge into the gap beside a gallery.
            foreach (var house in housing.Buildings)
                if ((house.Lot.BuildingVariant == 0 && house.Bank == CityTerraceTransportBank.West) ||
                    house.Lot.BuildingVariant == 2 || house.HasAirer)
                    AtFrontage(house);
            foreach (var house in housing.SharedCourtBuildings)
            {
                if (house.Lot.BuildingVariant != 3) AtFrontage(house);
                if (house.Lot.IsPlayerHome)
                {
                    // The imported front brick plinth projects 14 cm from the door datum.
                    var repair = house.ThresholdWorld + house.Lot.FacadeRotation * new Vector3(-3.9f, -.08f, .144f);
                    sites.Add(new Site(Kind.PlinthRepair, repair, house.Lot.FacadeRotation));
                }
            }

            var flight = stairs.SharedCourt.Flights[0];
            var stairSide = flight.Start + flight.Right * 2.7f;
            var stairEdge = new Site(Kind.RetainingEnd, Ground(stairSide) - Vector3.up * .10f,
                Quaternion.LookRotation(-flight.Right));
            if (Clear(stairEdge)) sites.Add(stairEdge);

            if (crestStop != null && layout.HasSummitHome)
            {
                var summit = quarter.Links[1];
                float x = Mathf.Lerp(crestStop.ShelterPosition.x, layout.SummitHome.ThresholdWorld.x, .42f);
                Garden(new Vector3(x, 0f, summit.Bounds.yMin - 2.9f), Vector3.forward);
            }
            foreach (var house in housing.Buildings)
            {
                if (house.Bank != CityTerraceTransportBank.East || house.Lot.BuildingVariant != 2) continue;
                // Two long returns on the balcony side have soil beside the end gable.
                int shelf = -house.Lot.Cell.y - 2;
                if (shelf != 2 && shelf != 5) continue;
                Rect body = house.FoundationBounds;
                float side = shelf == 2 ? 1f : -1f;
                var front = house.Lot.FacadeForward;
                for (int attempt = 0; attempt < 2; attempt++, side = -side)
                {
                    var at = new Vector3(body.center.x + side * (body.width * .5f + 6.8f),
                        0f, body.center.y + front.z * (body.height * .5f - 1.8f));
                    if (Garden(at, front)) break;
                }
            }
            foreach (var bridge in quarter.Bridges)
            {
                bool upper = bridge.Id == "summit";
                if (upper)
                {
                    // Across the street from Home, turn the retaining face along the lip.
                    var shoulder = Ground(new Vector3(bridge.WestBearingWorld.x - 3.2f, 0f,
                        bridge.DeckBounds.yMax + 4.4f));
                    var end = new Site(Kind.RetainingEnd, shoulder - Vector3.up * .34f,
                        Quaternion.LookRotation(Vector3.right));
                    if (Clear(end)) sites.Add(end);
                }
                float z = upper ? bridge.DeckBounds.yMax + 2.3f : bridge.DeckBounds.yMin - 2.2f;
                var section = CityHillsideQuarterRavine.At(z);
                // The first west and second east benches exist in the rock section;
                // the stones follow each ledge along the ravine, below the bearing.
                float u = upper ? .186f : .756f;
                Vector3 centre = Ground(new Vector3(Mathf.Lerp(section.y, section.z, u), 0f, z));
                float dx = .12f;
                var across = Ground(centre + Vector3.right * dx) - Ground(centre - Vector3.right * dx);
                var along = Ground(centre + Vector3.forward * dx) - Ground(centre - Vector3.forward * dx);
                Vector3 normal = Vector3.Cross(along, across).normalized;
                var facing = Vector3.ProjectOnPlane(upper ? Vector3.right : Vector3.left, normal);
                sites.Add(new Site(Kind.BearingTalus, centre - normal * .08f, Quaternion.LookRotation(facing, normal)));
            }

            // The open gates and the view from the balcony face along the street.
            // Keep the crowns off that axis, on the side parcels and behind gables.
            foreach (var house in housing.SharedCourtBuildings)
            {
                var b = house.FoundationBounds;
                if (house.Lot.IsPlayerHome)
                {
                    Tree(b.xMin - 11.6f, b.yMax - 5.8f, 1, 1.07f, 23f);
                    Tree(b.xMin - 7.3f, b.yMax - 9.6f, 3, .87f, 148f);
                    Tree(b.xMin - 4.6f, b.yMax + 20.6f, 0, .94f, 68f);
                }
                else if (house.HasAirer)
                {
                    // The residents' store turns toward the gallery; the trees stand
                    // beyond it, where their crowns flank the view from the street.
                    var p = new Vector3(b.xMax + 2.8f, 0f, b.yMin + 4.1f);
                    var shed = new Site(Kind.YardShelter, Ground(p) - Vector3.up * .045f,
                        Quaternion.Euler(0f, -85f, 0f));
                    if (OnTerrain(shed.Bounds) && Clear(shed)) sites.Add(shed);
                    Tree(b.xMin - 7.4f, b.yMin + 5.4f, 2, .92f, 90f);
                    Tree(b.xMin - 10.9f, b.yMin + 10.7f, 0, 1.05f, 18f);
                    Tree(b.xMax + 7.7f, b.yMin + 6.5f, 3, 1f, 201f);
                }
                else if (house.OnSummitStreet)
                {
                    Tree(b.xMax + 4.1f, b.yMax - 6.4f, 3, 1.05f, 162f);
                    Tree(b.xMax + 8.6f, b.yMax - 8.4f, 0, .84f, 41f);
                }
            }
            foreach (var house in housing.Buildings)
            {
                int shelf = -house.Lot.Cell.y - 2;
                bool east = house.Bank == CityTerraceTransportBank.East;
                if (house.Lot.BuildingVariant != (east ? 2 : 0)) continue;
                var b = house.FoundationBounds;
                float side = (shelf & 1) == 0 ? 1f : -1f;
                float end = side > 0 ? b.xMax : b.xMin;
                Tree(end + side * 6.6f, b.center.y + (east ? 2.9f : -1.8f),
                    (shelf + (east ? 1 : 2)) % 4, .91f + shelf % 3 * .06f, 51f + shelf * 71f);
                if (shelf % 3 == (east ? 0 : 1))
                    Tree(end + side * 11.5f, b.center.y - 2.6f, (shelf + 3) % 4, .85f, 163f + shelf * 19f);
            }
            return sites;

            bool OnTerrain(Rect area)
            {
                var lo = area.min;
                var hi = area.max;
                if (quarter.TerrainBounds.Contains(lo) && quarter.TerrainBounds.Contains(hi)) return true;
                foreach (var grid in banks)
                    if (grid.TryHeight(lo, out _) && grid.TryHeight(hi, out _)) return true;
                return false;
            }

            void Tree(float x, float z, int variant, float scale, float yaw)
            {
                Vector3 at = Ground(new Vector3(x, 0f, z));
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI * .25f;
                    at.y = Mathf.Min(at.y, Ground(at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (.34f * scale)).y);
                }
                var tree = new Site(Kind.YardTree, at - Vector3.up * .015f,
                    Quaternion.Euler(0f, yaw, 0f), variant, scale);
                if (OnTerrain(tree.Bounds) && Clear(tree)) sites.Add(tree);
            }

            bool Garden(Vector3 at, Vector3 front)
            {
                var right = Vector3.Cross(Vector3.up, front);
                var across = Ground(at + right * 4.8f) - Ground(at - right * 4.8f);
                var depth = Ground(at + front) - Ground(at - front);
                var normal = Vector3.Cross(depth, across).normalized;
                if (normal.y < .95f) return false;
                var site = new Site(Kind.GardenVerge, Ground(at) - Vector3.up * .035f,
                    Quaternion.LookRotation(Vector3.ProjectOnPlane(front, normal), normal));
                if (!Clear(site)) return false;
                sites.Add(site);
                return true;
            }

            void AtFrontage(CityHillsideBuilding house)
            {
                bool west = house.Bank == CityTerraceTransportBank.West;
                var facing = house.Lot.FacadeRotation;
                var right = facing * Vector3.right;
                var front = facing * Vector3.forward;
                float span = west ? 3f : 1.90f;
                float halfFront = Mathf.Abs(right.x) > .5f ? house.FoundationBounds.width * .5f : house.FoundationBounds.height * .5f;
                Rect body = house.FoundationBounds;
                float frontageDepth = Mathf.Abs(front.x) > .5f ? body.width * .5f : body.height * .5f;
                Vector3 wall = new Vector3(body.center.x, 0f, body.center.y) + front * frontageDepth;
                float side = house.Lot.IsPlayerHome || (house.Lot.Cell.y & 1) == 0 ? -1f : 1f;
                for (int attempt = 0; attempt < 2; attempt++, side = -side)
                {
                    // Leave the planted side of a court edge in soil beyond the asphalt band.
                    float endGap = west ? .50f : CityBuildingPerimeterPlan.Width + .10f;
                    Vector3 at = wall + right * (side * (Mathf.Max(3.8f, halfFront + endGap) + span)) +
                        front * (west ? .45f : .70f);
                    var site = new Site(west ? Kind.RetainingEnd : Kind.CourtEdge,
                        Ground(at) - Vector3.up * (west ? .09f : .025f), facing);
                    if (!Clear(site)) continue;
                    sites.Add(site);
                    return;
                }
            }

            Vector3 Ground(Vector3 at)
            {
                Vector2 p = new Vector2(at.x, at.z);
                foreach (var grid in banks)
                    if (grid.TryHeight(p, out at.y)) return at;
                at.y = quarter.SampleSoil(p);
                return at;
            }

            bool Clear(Site site)
            {
                // Reserve a capsule beside every public path and threshold. A
                // second candidate can use the other side of the same frontage.
                Rect r = site.Bounds;
                r = Rect.MinMaxRect(r.xMin - .40f, r.yMin - .40f, r.xMax + .40f, r.yMax + .40f);
                foreach (var occupied in reserved) if (r.Overlaps(occupied)) return false;
                foreach (var placed in sites) if (r.Overlaps(placed.Bounds)) return false;
                foreach (var link in quarter.Links) if (r.Overlaps(link.Bounds)) return false;
                foreach (var open in quarter.ExposedRavinePolygons)
                    if (r.Overlaps(CityRoadPolygon.Bounds(open))) return false;
                foreach (var grid in banks)
                {
                    if (!grid.Road.WorldBounds.Overlaps(r)) continue;
                    foreach (var patch in grid.Road.SurfacePatches)
                        if (r.Overlaps(patch.Bounds)) return false;
                }
                return true;
            }
        }

        internal static void Build(Transform parent, CityLayout layout, Rect? retained = null)
        {
            var sites = ForLayout(layout);
            if (sites.Count == 0) return;
            var templates = new GameObject[Names.Length];
            var root = new GameObject("Residential terrace edges").transform;
            root.SetParent(parent, false);
            foreach (var site in sites)
            {
                if (retained.HasValue && !retained.Value.Overlaps(site.Bounds)) continue;
                int i = (int)site.Part;
                var frame = new GameObject(Names[i]).transform;
                frame.SetParent(root, false);
                frame.localPosition = site.Origin;
                frame.localRotation = site.Rotation;
                if (site.Part == Kind.YardTree)
                {
                    frame.localScale = Vector3.one * site.Scale;
                    BuildTree(frame, site.Variant, !retained.HasValue);
                    continue;
                }
                if (site.Part == Kind.YardShelter)
                {
                    const CityResidentialCourtyardPropKind shelter = CityResidentialCourtyardPropKind.UtilityShelter;
                    CityResidentialCourtyardAssetProvider.Create(frame, shelter, frame.position, frame.rotation);
                    if (!retained.HasValue)
                    {
                        var solid = frame.gameObject.AddComponent<BoxCollider>();
                        var bounds = CityResidentialCourtyardAssetProvider.GetLocalBounds(shelter);
                        solid.center = bounds.center;
                        solid.size = bounds.size;
                    }
                    continue;
                }
                if (!templates[i]) templates[i] = Resources.Load<GameObject>("City/Hillside/" + Names[i]) ??
                    throw new InvalidOperationException("Missing hillside asset " + Names[i]);
                var model = UnityEngine.Object.Instantiate(templates[i], frame, false);
                foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>()) Bind(renderer);
                // Only the solid authored bodies can meet a capsule. The physical
                // ravine owns its rocks and the apartment replay is wholly passive.
                if (retained.HasValue) continue;
                if (site.Part == Kind.RetainingEnd || site.Part == Kind.GardenVerge)
                {
                    foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                        if (!filter.name.Contains("__SteelDark", StringComparison.Ordinal) &&
                            !filter.name.Contains("__Foliage", StringComparison.Ordinal))
                            filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                }
                else if (site.Part == Kind.CourtEdge)
                {
                    var body = frame.gameObject.AddComponent<BoxCollider>();
                    body.center = new Vector3(0f, .005f, -.15f);
                    body.size = new Vector3(3.8f, .57f, .24f);
                }
            }
        }

        private static void BuildTree(Transform frame, int variant, bool solid)
        {
            var provider = CityMiscAssetProvider.LoadOrThrow();
            int count = CityMiscAssetProvider.GetPartCount(CityMiscKind.ParkTree);
            for (int i = 0; i < count; i++)
            {
                var part = provider.GetPartOrThrow(CityMiscKind.ParkTree, variant, i);
                var branch = new GameObject(part.Component);
                branch.transform.SetParent(frame, false);
                branch.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                var renderer = branch.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                bool bark = part.Role == CityMiscMeshRole.Bark;
                CityParkSurfaceAppearance.ApplyCombined(renderer, bark ? CityParkSurfaceKind.Bark : CityParkSurfaceKind.Foliage,
                    bark ? new Color(.31f, .27f, .21f) : new Color(.28f, .33f, .21f));
            }
            if (!solid) return;
            var trunk = frame.gameObject.AddComponent<BoxCollider>();
            trunk.center = Vector3.up * 1.15f;
            trunk.size = new Vector3(.68f, 2.3f, .68f);
        }

        private static void Bind(MeshRenderer renderer)
        {
            string name = renderer.name;
            int start = name.LastIndexOf("__", StringComparison.Ordinal) + 2;
            int suffix = name.IndexOf('.', start);
            string role = suffix < 0 ? name.Substring(start) : name.Substring(start, suffix - start);
            switch (role)
            {
                case "Rock":
                    CityMountainSurfaceAppearance.ApplyCombined(renderer, new Color(.41f, .42f, .39f));
                    break;
                case "Soil":
                    CityExteriorAppearance.ApplyGroundSurface(renderer, CityHillsidePerimeterSurface.ResidentialSoil);
                    break;
                case "Foliage":
                    renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                    CityParkSurfaceAppearance.ApplyCombined(renderer, CityParkSurfaceKind.Foliage, new Color(.37f, .42f, .30f));
                    break;
                case "Bark":
                    renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                    CityParkSurfaceAppearance.ApplyCombined(renderer, CityParkSurfaceKind.Bark, new Color(.34f, .31f, .26f));
                    break;
                case "DampMortar":
                case "RepairMortar":
                    bool damp = role == "DampMortar";
                    CityFringeYardSurfaceAppearance.ApplyCombined(renderer, CityFringeYardSurfaceKind.Concrete,
                        damp ? new Color(.35f, .36f, .32f) : new Color(.56f, .535f, .47f));
                    break;
                default:
                    renderer.sharedMaterial = CityPortAssetProvider.GetSurfaceMaterial(role);
                    if (role == "Concrete" || role == "ConcreteWall")
                        GroundAuthoredSurfaceAppearance.Apply(renderer, GroundSurfaceKind.Concrete, cityClimate: true);
                    break;
            }
            CitySurfaceLibrary.ApplyRenderer(renderer);
        }
    }
}
