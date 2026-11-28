using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal enum CityLeafHabitat { Park, Ridge, Birch, Garden, Fence, Paving, StreetEdge, OpenGround }

    /// <summary>Crowns shed leaves. The other sites hold leaves already carried to street level.</summary>
    internal readonly struct CityLeafSite
    {
        internal readonly Vector3 Position;
        internal readonly float CrownHeight;
        internal readonly CityLeafHabitat Habitat;
        internal readonly uint Seed;
        internal const int MaximumLeaves = 146;
        internal int Count => (Habitat switch
        {
            CityLeafHabitat.Park => 96, CityLeafHabitat.Ridge => 78, CityLeafHabitat.Birch => 76,
            CityLeafHabitat.OpenGround => 76,
            CityLeafHabitat.Fence or CityLeafHabitat.Garden => 64, CityLeafHabitat.Paving => 58, _ => 42
        }) + (int)(Seed % 25) + (CrownHeight > 6f ? 26 : 0);
        internal int CrownLeaves => CrownHeight > 0f ? (CrownHeight > 6f ? 8 : 5) : 0;

        internal CityLeafSite(Vector3 position, float crownHeight, CityLeafHabitat habitat, int seed)
        {
            Position = position;
            CrownHeight = crownHeight;
            Habitat = habitat;
            Seed = CityGrassPlan.Hash(seed, Mathf.RoundToInt(position.x * 10f),
                Mathf.RoundToInt(position.z * 10f), (uint)habitat + 753u);
        }

        internal static List<CityLeafSite> Build(CityLayout city, bool lower, bool ridge,
            CityCemeteryPlan cemetery, HomeExteriorContextPlan home = null)
        {
            var sites = new List<CityLeafSite>();
            void Add(Vector3 p, CityLeafHabitat habitat, float height = 0f)
            {
                if (home != null && (p - home.PlayerHome.DoorPosition).sqrMagnitude >
                    HomeExteriorContextPlanner.ViewRadius * HomeExteriorContextPlanner.ViewRadius) return;
                sites.Add(new CityLeafSite(p, height, habitat, city.Seed));
            }

            if (lower)
            {
                var plans = CityWorldPlans.GetOrCreate(city);
                for (int i = 0; i < city.Park.TreePositions.Count; i++)
                {
                    Vector3 root = city.Park.TreePositions[i];
                    Add(root, CityLeafHabitat.Park, new CityTreeForm(city.Seed, root, i % 4).LeafHeight);
                }
                foreach (var gate in city.Park.Gates) Add(gate.Center, CityLeafHabitat.Paving);
                foreach (var bench in city.Park.Benches) Add(bench.Position + bench.Forward * .9f, CityLeafHabitat.Paving);
                if (cemetery != null)
                    foreach (var part in cemetery.Parts)
                        if (part.Style == CityCemeteryStyle.TrunkBirch)
                            Add(part.Center - Vector3.up * (.5f * part.Size.y), CityLeafHabitat.Birch, 3.2f * part.Size.y / 2.9f);
                if (plans.ChurchCourtyard != null)
                    foreach (var fixture in plans.ChurchCourtyard.Fixtures)
                        if (fixture.Kind == CityChurchCourtyardFixtureKind.Tree)
                            Add(fixture.GroundPosition, CityLeafHabitat.Garden, 3.5f * fixture.Scale.y);
                if (plans.EastExit != null)
                    foreach (var tree in CityEastTreePlan.Create(plans.EastExit).Parts)
                        Add(tree.Position, CityLeafHabitat.Fence, tree.Height * .72f);

                foreach (var court in CityResidentialCourtyardPlan.ForLayout(city).Courts)
                {
                    Add(court.GroupPosition + court.GroupForward * .85f, CityLeafHabitat.Paving);
                    if (court.ConnectionPath.Count > 0) Add(court.ConnectionPath[0], CityLeafHabitat.Paving);
                }
                foreach (var lot in city.Lots)
                {
                    if (!lot.HasRoadFrontage || !lot.HasBuilding) continue;
                    uint choice = CityGrassPlan.Hash(city.Seed, lot.Cell.x, lot.Cell.y, 7181);
                    int interval = lot.District == CityDistrictKind.Industrial ? 4 : 3;
                    if (choice % interval != 0 && !lot.IsBar && !lot.IsDistrictPointOfInterest) continue;
                    Vector3 tangent = Vector3.Cross(Vector3.up, lot.FacadeForward);
                    Add(lot.SidewalkArrivalPosition + tangent * ((choice & 1) == 0 ? 2f : -2f), CityLeafHabitat.StreetEdge);
                }
                if (plans.Fair.IsEnabled) Add(plans.Fair.CenterPathSouth, CityLeafHabitat.Paving);
                if (plans.ArchShelter.IsEnabled)
                {
                    var arch = plans.ArchShelter.Placement;
                    Add(new Vector3(arch.PassageFootprint.center.x, arch.WestSurfaceY,
                        arch.PassageFootprint.yMax + .65f), CityLeafHabitat.StreetEdge);
                }
                foreach (var bridge in city.River.Bridges)
                    if (bridge.Definition.Role == CityBridgeRole.ParkFootbridge)
                        Add(new Vector3(bridge.DeckBounds.xMin + .35f, bridge.WestY,
                            bridge.DeckBounds.center.y), CityLeafHabitat.Paving);
                foreach (var landing in city.River.Landings)
                    Add(new Vector3(landing.PlatformBounds.center.x, landing.LowerY,
                        landing.PlatformBounds.center.y), CityLeafHabitat.StreetEdge);
                foreach (var access in city.OpenAreaAccesses)
                    if (access.Feature is CityAreaFeatureKind.NorthWaterfront or CityAreaFeatureKind.Yard)
                        Add(access.Center - access.OutwardNormal * .4f, CityLeafHabitat.StreetEdge);
            }
            if (ridge)
            {
                var garage = RidgeGaragePlan.ForLayout(city);
                var parking = RidgeParkingPlan.ForLayout(city);
                foreach (var site in CityHillsideSiteDetails.ForLayout(city))
                    if (site.Part == CityHillsideSiteDetails.Kind.YardTree &&
                        garage?.Bounds.Overlaps(site.Bounds) != true && parking?.Clears(site) != true)
                        Add(site.Origin, CityLeafHabitat.Ridge, 3.9f * site.Scale);
                foreach (var plant in CityRidgeVegetationPlan.ForLayout(city).Plants)
                    if (plant.IsTree) Add(plant.Origin, CityLeafHabitat.Ridge, plant.Form.Value.LeafHeight);
                foreach (var landing in CityPawnshopDescentPlan.ForLayout(city).Landings)
                    Add(landing.End, CityLeafHabitat.Paving);
                foreach (var link in CityHillsideQuarterPlan.ForLayout(city).Links)
                    if (link.NorthSidewalk.Count > 0)
                    {
                        Add(link.NorthSidewalk[0], CityLeafHabitat.StreetEdge);
                        Add(link.NorthSidewalk[link.NorthSidewalk.Count - 1], CityLeafHabitat.StreetEdge);
                    }
                if (garage != null) Add(garage.LowerArrival, CityLeafHabitat.StreetEdge);
                if (RidgeMotelPlan.ForLayout(city) is { } motel) Add(motel.Arrival, CityLeafHabitat.StreetEdge);
                var glass = CityGlassReturnSitePlan.ForLayout(city);
                if (glass.IsEnabled) Add(glass.Approach, CityLeafHabitat.StreetEdge);
                var cobbler = CityCobblerSitePlan.ForLayout(city);
                if (cobbler.IsEnabled) Add(cobbler.Approach, CityLeafHabitat.StreetEdge);
            }
            foreach (var kiosk in CityKioskSites.ForLayout(city).Sites)
                if (kiosk.IsRidge ? ridge : lower) Add(kiosk.PublicThroughRight, CityLeafHabitat.StreetEdge);
            return sites;
        }
    }
}
