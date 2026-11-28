using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Additional planting in the gaps left by the established Ridge sites.</summary>
    internal sealed class CityRidgeVegetationPlan
    {
        internal const string RootName = "Ridge vegetation";
        internal const int MaximumTrees = 106;
        internal const int MaximumShrubs = 54;
        private static readonly ConditionalWeakTable<CityLayout, CityRidgeVegetationPlan> Plans = new();
        private readonly List<Plant> plants = new(MaximumTrees + MaximumShrubs);
        private readonly List<Vector2[]> trunks = new(MaximumTrees);
        internal IReadOnlyList<Plant> Plants => plants;
        internal IReadOnlyList<Vector2[]> Trunks => trunks;

        internal static CityRidgeVegetationPlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, city => new CityRidgeVegetationPlan(city));

        internal readonly struct Plant
        {
            internal readonly CityMiscKind Kind;
            internal readonly CityTerraceTransportBank Bank;
            internal readonly Vector3 Origin;
            internal readonly Quaternion Rotation;
            internal readonly Rect Bounds;
            internal readonly float Scale;
            internal readonly int Variant;
            internal readonly CityTreeForm? Form;
            internal readonly bool OnOpenEarth;
            internal bool IsTree => Kind == CityMiscKind.ParkTree;

            internal Plant(CityMiscKind kind, CityTerraceTransportBank bank, Vector3 origin,
                Quaternion rotation, Rect bounds, float scale, int variant, CityTreeForm? form, bool onOpenEarth)
            {
                Kind = kind; Bank = bank; Origin = origin; Rotation = rotation;
                Bounds = bounds; Scale = scale; Variant = variant; Form = form; OnOpenEarth = onOpenEarth;
            }
        }

        private CityRidgeVegetationPlan(CityLayout layout)
        {
            var quarter = CityHillsideQuarterPlan.ForLayout(layout);
            if (quarter.Links.Count == 0) return;
            var houses = CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings;
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            var streets = CityStreetSupportPlan.ForLayout(layout);
            var network = CityCoastalInfrastructurePlan.ForLayout(layout);
            var occupied = new List<Rect>(network.SolidFootprints);
            var nearby = new List<CityStreetSupportPatch>();
            var cellars = RidgeCellarPlan.ForLayout(layout);
            var bendSidewalks = CityHillsideStreetPlan.ForLayout(layout).SidewalkExtensions;
            foreach (var house in houses)
            {
                occupied.Add(house.Lot.IsPlayerHome ? Expanded(house.FoundationBounds, 3.5f) : house.FoundationBounds);
                occupied.Add(house.CourtBounds);
                occupied.Add(house.ApproachBounds);
            }
            foreach (var site in CityHillsideSiteDetails.ForLayout(layout)) occupied.Add(site.Bounds);
            foreach (var stair in CityHillsideAccessPlan.ForLayout(layout).AllStairs)
                occupied.AddRange(stair.WalkableBounds);
            foreach (var bridge in quarter.Links) occupied.Add(bridge.Bounds);
            foreach (var item in CityHillsideFurniturePlan.ForLayout(layout).Placements)
                occupied.Add(item.AccessBounds);
            occupied.AddRange(CityBusNetworkFurniture.Reservations(CityDistrictBusPlanner.CreateRoadRouting(layout), layout));
            if (RidgeRavinePlan.ForLayout(layout) is { } ruin) occupied.Add(ruin.Bounds);
            if (RidgeMotelPlan.ForLayout(layout) is { } motel) occupied.Add(motel.Court);
            if (RidgeGaragePlan.ForLayout(layout) is { } garage) occupied.Add(garage.Bounds);
            if (RidgeParkingPlan.ForLayout(layout) is { } parking) occupied.Add(parking.SurfaceBounds);
            if (RidgeKioskSitePlan.TryCreate(layout, out var kiosk))
            {
                occupied.Add(kiosk.ReservedBounds);
                if (kiosk.Court != null) occupied.AddRange(kiosk.Court.Reservations);
            }
            if (CityPawnshopPlan.TryCreate(layout, out var pawnshop))
            {
                occupied.Add(pawnshop.Bounds);
                occupied.Add(CityRoadPolygon.Bounds(pawnshop.ApproachFootprint));
            }
            foreach (var pocket in CityHillsideBenchPocketPlan.ForLayout(layout).Pockets)
                foreach (var path in pocket.SurfacePolygons) occupied.Add(CityRoadPolygon.Bounds(path));
            foreach (var path in CityPawnshopBenchTerracePlan.ForLayout(layout).Footprints)
                occupied.Add(CityRoadPolygon.Bounds(path));
            foreach (var path in CityPawnshopDescentPlan.ForLayout(layout).Footprints)
                occupied.Add(CityRoadPolygon.Bounds(path));
            var loft = CityPigeonLoftPlan.ForLayout(layout);
            if (loft.IsEnabled) occupied.Add(loft.Courtyard.Bounds);
            if (layout.HasSummitHome) occupied.Add(CityGlassReturnSitePlan.LandReservation);
            var cobbler = CityCobblerSitePlan.ForLayout(layout);
            if (cobbler.IsEnabled) occupied.Add(cobbler.Bounds);

            foreach (var bank in CityHillsideStreetPlan.ForLayout(layout).Routes)
            for (int tier = CityHillsideStreetRoute.LaneCount - 1; tier >= 0; tier--)
            {
                int remaining = tier >= 5 ? 2 : 1;
                foreach (var house in houses)
                {
                    if (house.Bank != bank.Bank || house.Lot.IsPlayerHome ||
                        Mathf.Abs(house.StreetContactWorld.z - bank.GetLaneZ(tier)) > 5f) continue;
                    if (AddGroup(house) && --remaining == 0) break;
                }
            }

            int treeCount = plants.Count;
            for (int index = 0; index < treeCount; index++)
            {
                Plant tree = plants[index];
                Vector2 centre = new(tree.Origin.x, tree.Origin.z);
                int x = Mathf.RoundToInt(centre.x * 4f), z = Mathf.RoundToInt(centre.y * 4f);
                float phase = CityGrassPlan.Unit(CityGrassPlan.Hash(layout.Seed, x, z, 733)) * 2f * Mathf.PI;
                int remaining = 3;
                for (int attempt = 0; attempt < 14; attempt++)
                {
                    uint shrub = CityGrassPlan.Hash(layout.Seed, x, z, 517u + (uint)attempt);
                    float angle = phase + attempt * 2.39996f;
                    Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) *
                        Mathf.Lerp(2.85f, 5.4f, CityGrassPlan.Unit(shrub >> 16));
                    CityMiscKind kind = (shrub & 1u) == 0 ? CityMiscKind.CemeteryBush : CityMiscKind.ChurchCourtyardShrub;
                    if (TryPlant(kind, tree.Bank, centre + offset, shrub) && --remaining == 0) break;
                }
            }

            // The established courtyard groups keep their shrub pockets. Scatter the remaining trees
            // across actual earth cells on both banks and the ground between them.
            foreach (var grid in CityHillsideGroundGrid.ForSurface(layout))
                Scatter(CityHillsideNeighborhoodPlan.TerrainBounds(grid.Road), grid.Road.Bank);
            Scatter(quarter.TerrainBounds, null);

            void Scatter(Rect area, CityTerraceTransportBank? bank)
            {
                foreach (Vector2 point in CitySoilTrees.Candidates(area, layout.Seed))
                {
                    if (trunks.Count >= MaximumTrees) break;
                    if (!ground.IsEarth(point)) continue;
                    uint hash = CityGrassPlan.Hash(layout.Seed, Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y), 724);
                    var side = bank ?? (point.x < area.center.x ? CityTerraceTransportBank.West : CityTerraceTransportBank.East);
                    TryPlant(CityMiscKind.ParkTree, side, point, hash, true);
                }
            }

            bool AddGroup(CityHillsideBuilding house)
            {
                Rect body = house.FoundationBounds;
                Vector2Int cell = house.Lot.Cell;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    uint variation = CityGrassPlan.Hash(layout.Seed, cell.x, cell.y, 307u + (uint)attempt);
                    float side = ((attempt ^ (int)CityGrassPlan.Hash(layout.Seed, cell.x, cell.y, 941)) & 1) == 0 ? -1f : 1f;
                    float gap = 3.6f + (attempt / 2) * 1.35f;
                    float x = (side < 0f ? body.xMin : body.xMax) + side * gap;
                    float z = Mathf.Lerp(body.yMin, body.yMax, .20f + .67f * CityGrassPlan.Unit(variation));
                    if (TryPlant(CityMiscKind.ParkTree, house.Bank, new Vector2(x, z), variation)) return true;
                }
                return false;
            }

            bool TryPlant(CityMiscKind kind, CityTerraceTransportBank bank, Vector2 point, uint variation, bool openEarth = false)
            {
                bool tree = kind == CityMiscKind.ParkTree;
                bool broadShrub = kind == CityMiscKind.ChurchCourtyardShrub;
                float scale = tree ? Mathf.Lerp(.79f, 1.04f, CityGrassPlan.Unit(variation)) :
                    Mathf.Lerp(broadShrub ? .90f : 1.06f, broadShrub ? 1.27f : 1.52f, CityGrassPlan.Unit(variation));
                int variant = tree ? (int)(variation >> 18) % CityMiscAssetProvider.GetVariantCount(kind) : 0;
                var form = tree ? new CityTreeForm(layout.Seed, new Vector3(point.x, 0f, point.y), variant) : (CityTreeForm?)null;
                float radius = form?.Radius ?? .70f * scale;
                float plantingClearance = form?.TrunkRadius + .25f ?? radius;
                if (cellars?.BlocksPlant(point, plantingClearance) == true) return false;
                foreach (var bend in bendSidewalks)
                    if (bend.BlocksPlant(point, plantingClearance)) return false;
                float sway = tree ? CityWindField.MaximumTreeTravel : CityWindField.MaximumShrubTravel;
                Rect crown = new(point.x - radius, point.y - radius, radius * 2f, radius * 2f);
                Rect clearance = Expanded(crown, sway + .25f);
                foreach (Rect reservation in occupied)
                    if (clearance.Overlaps(reservation)) return false;
                if (quarter.IsExposedRavine(point, radius) || !ground.Contains(point, .42f) ||
                    !ground.TrySampleTop(point, out float floor)) return false;
                nearby.Clear();
                streets.GetNearby(clearance, nearby);
                foreach (var patch in nearby)
                    if (clearance.Overlaps(patch.Bounds)) return false;

                float rootRadius = form?.TrunkRadius ?? .30f * scale;
                float rootY = floor;
                for (int sample = 0; sample < 4; sample++)
                {
                    float angle = sample * Mathf.PI * .5f;
                    Vector2 rootEdge = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * rootRadius;
                    if (openEarth && !ground.IsEarth(rootEdge) || !ground.TrySampleTop(rootEdge, out float top) || Mathf.Abs(top - floor) > .30f) return false;
                    rootY = Mathf.Min(rootY, top);
                }
                float height = form?.Height ?? scale * (broadShrub ? .86f : .62f);
                foreach (var span in network.CableSpans)
                    if (TouchesCable(span, point, rootY + height, radius + sway)) return false;

                var origin = new Vector3(point.x, rootY - .015f, point.y);
                Quaternion rotation = form?.Rotation ?? Quaternion.Euler(0f, CityGrassPlan.Unit(variation >> 16) * 360f, 0f);
                plants.Add(new Plant(kind, bank, origin, rotation, crown, tree ? 1f : scale, variant, form, openEarth));
                occupied.Add(crown);
                if (tree) trunks.Add(CityHillsideSiteDetails.TrunkFootprint(origin, rotation, form.Value.Wood.x));
                return true;
            }
        }

        internal static bool TouchesCable(CityCoastalCableSpan span, Vector2 root, float top, float radius)
        {
            Vector2 start = new(span.Start.x, span.Start.z);
            Vector2 delta = new(span.End.x - span.Start.x, span.End.z - span.Start.z);
            float length = delta.magnitude;
            float clearance = radius + span.WindSwing + .25f;
            float t = Mathf.Clamp01(Vector2.Dot(root - start, delta) / Mathf.Max(.00001f, length * length));
            if ((root - start - delta * t).sqrMagnitude > clearance * clearance) return false;
            float reach = clearance / Mathf.Max(.001f, length);
            float low = (4f * span.Sag - (span.End.y - span.Start.y)) / Mathf.Max(.001f, 8f * span.Sag);
            low = Mathf.Clamp(low, Mathf.Clamp01(t - reach), Mathf.Clamp01(t + reach));
            return span.Sample(low).y <= top + .25f;
        }

        private static Rect Expanded(Rect rect, float amount) => Rect.MinMaxRect(
            rect.xMin - amount, rect.yMin - amount, rect.xMax + amount, rect.yMax + amount);

        internal void Build(Transform parent, Rect? retained = null)
        {
            if (plants.Count == 0) return;
            var root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            foreach (Plant plant in plants)
            {
                if (retained.HasValue && !retained.Value.Overlaps(plant.Bounds)) continue;
                var instance = new GameObject(plant.IsTree ? "Yard tree" : "Yard shrub").transform;
                instance.SetParent(root, false);
                instance.localPosition = plant.Origin;
                instance.localRotation = plant.Rotation;
                instance.localScale = Vector3.one * plant.Scale;
                CityHillsideSiteDetails.BuildPlant(instance, plant.Kind, plant.Variant, !retained.HasValue, plant.Form);
            }
        }
    }
}
