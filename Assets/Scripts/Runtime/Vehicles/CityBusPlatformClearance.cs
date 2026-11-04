using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Static obstructions known before service is routed. Back houses,
    /// delivery furniture and litter reserve the published platforms in their own plans.</summary>
    public sealed class CityBusPlatformClearance
    {
        private const float CellSize = 16f;
        // Start on public paving beyond both the stopped body and the shelter's
        // rear posts. A clear patch between the two doors alone is not an approach.
        internal const float PavementApproachLength = 6.5f;
        internal const float PavementOffsetFromLane = 2.05f;
        private static readonly ConditionalWeakTable<CityLayout, CityBusPlatformClearance> Plans = new();
        private readonly Dictionary<Vector2Int, List<Rect>> cells = new();
        private readonly CityLayout layout;
        private readonly CityStreetSupportPlan paving;
        private readonly IReadOnlyList<CityHillsideGroundGrid> hillsideGround;
        private readonly Dictionary<Vector2, float> sampledHeights = new();

        public static CityBusPlatformClearance ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityBusPlatformClearance(value));

        private CityBusPlatformClearance(CityLayout layout)
        {
            // Never request CityWorldPlans, a finished street surface or residential
            // infill here: their planners consume the bus network. These upstream
            // recipes are also used during raw residential infill siting.
            this.layout = layout;
            paving = CityStreetSupportPlan.ForLayout(layout);
            hillsideGround = CityHillsideGroundGrid.ForLayout(layout);
            RoadFencePlan fences = RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan fixtures = CityLayoutCache.GetOrCreateNightPlan(layout);
            var scenery = CityDecorationPlanner.CreatePlan(layout, fences, fixtures);
            foreach (var segment in fences.Segments)
                Add(Envelope(segment.Start + segment.OutwardNormal * .08f, segment.End + segment.OutwardNormal * .08f, .09f));
            foreach (var pole in fixtures.StreetLamps)
                Add(Xz(CityStaticCollisionBuilder.CreateLowerPoleBounds(pole.Position)));
            foreach (var signal in fixtures.TrafficSignals)
                Add(Xz(CityStaticCollisionBuilder.CreateLowerPoleBounds(signal.Position)));
            foreach (Vector3 tree in layout.Park.TreePositions)
                Add(Envelope(tree, tree, 1.75f)); // Crown and low branches, not just the trunk collider.
            foreach (var bench in layout.Park.Benches)
                Add(Envelope(bench.Position, bench.Position, CityParkBenchDescriptor.SeatWidth * .5f));

            if (layout.HasParkBoundaryHedges)
                foreach (Bounds hedge in CityWorldBuilder.DescribeParkHedges(layout.Park)) Add(Xz(hedge));
            foreach (var item in CityParkFurniturePlan.Create(layout, scenery).Placements)
                Add(Envelope(item.Position, item.Position, item.ClearanceRadius));

            // The cafe uses an open lot in the street grid. Its five-sided shell
            // and the street furniture around its entrance are authored separately.
            var cafe = CityCafePlan.Create(layout);
            if (cafe != null)
            {
                Add(cafe.Bounds);
                Add(cafe.ApproachBounds);
                var corners = new Vector2[4];
                foreach (var part in MountainRoadTerminalSitePlanner.CreateCafeSite(cafe.Cafe).Parts)
                {
                    part.GetFootprintCorners(corners);
                    Add(CityRoadPolygon.Bounds(corners));
                }
            }
            var church = CityChurchPlanner.Create(layout);
            var passage = CityChurchCemeteryPassagePlanner.Create(layout, church);
            var garden = CityChurchCourtyardPlanner.Create(layout, church, passage);
            if (church != null) { Add(church.ModelFootprint); Add(church.ApproachBounds); }
            if (garden != null)
                foreach (var item in garden.Fixtures) Add(item.BlockerBounds);
            var cemetery = CityCemeteryPlanner.Create(layout, passage);
            if (cemetery != null)
                foreach (var part in cemetery.Parts)
                    if (part.Kind != CityCemeteryPartKind.Alley)
                        Add(Xz(CityBuildingPrototypePlacement.TransformBounds(new Bounds(Vector3.zero, part.Size),
                            new CityBuildingPrototypePose(part.Center, part.Rotation))));

            var proxies = new List<Bounds>(CityStaticCollisionBuilder.MaximumDecorationProxyCount);
            foreach (var item in scenery.Descriptors)
            {
                if (item.CollisionTier == CityDecorationCollisionTier.None)
                {
                    if (item.AnchorKind == CityDecorationAnchorKind.Roadside || item.AnchorKind == CityDecorationAnchorKind.LotGround)
                        Add(Envelope(item.Position, item.Position, CityDecorationValidator.ResolveProtectionRadius(item.Kind)));
                    continue;
                }
                proxies.Clear();
                CityStaticCollisionBuilder.AddDecorationProxyBounds(layout, item, proxies);
                foreach (Bounds bounds in proxies) Add(Xz(bounds));
            }
            foreach (var house in layout.BuildingMasses)
            {
                if (!house.HasBuilding) continue;
                if (house.IsOrdinaryBuilding && (house.BuildingVariant > 0 || house.District == CityDistrictKind.Residential))
                {
                    // The ground-only pedestrian footprints omit the mass over a vestibule
                    // or arch. Keep the shelter roof clear of every actual Building Mass.
                    var pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(house);
                    foreach (Bounds mass in CityBuildingAssetProvider.GetExpectedCollisionBounds(house.District, house.BuildingVariant))
                        Add(Xz(CityBuildingPrototypePlacement.TransformBounds(mass, pose)));
                }
                else foreach (Rect solid in house.CreateCollisionFootprints()) Add(solid);
            }
            foreach (var place in layout.DistrictPointsOfInterest)
            {
                Add(place.PublicBounds);
                foreach (var access in place.Accesses) Add(access.ApproachBounds);
            }
            foreach (var access in layout.OpenAreaAccesses) Add(access.ApproachBounds);
            foreach (var home in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
            {
                Add(home.FoundationBounds);
                Add(home.ApproachBounds);
                Add(home.CourtBounds);
            }
            foreach (var stair in CityHillsideAccessPlan.ForLayout(layout).AllStairs)
                Add(stair.Bounds);
        }

        public bool Allows(CityBusStopDescriptor stop) =>
            Allows(stop.Position, stop.Forward, stop.ShelterPosition, stop.RoadsideForward);

        public bool Allows(Vector3 lanePosition, Vector3 forward, Vector3 shelterPosition, Vector3 roadsideForward)
        {
            Rect shelter = ShelterReservation(shelterPosition, forward, roadsideForward);
            if (!IsClear(shelter) || !IsClear(PassengerAccess(lanePosition, forward, shelterPosition, roadsideForward)) ||
                !IsClear(PavementApproach(lanePosition, forward, roadsideForward)))
                return false;
            // A sampling point behind the pole can be on much higher beach earth
            // than the road-side corner of the same shelter, even on a broad straight.
            // Check the whole actual footprint, with the margins used for its columns.
            Vector3 along = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            Vector3 across = Vector3.ProjectOnPlane(roadsideForward, Vector3.up).normalized;
            if (!TryHeight(shelterPosition, out float datum) || Mathf.Abs(datum - lanePosition.y) > .32f)
                return false;
            for (int i = 0; i < hillsideGround.Count; i++)
                if (hillsideGround[i].ReachesAbove(shelter, datum + .26f)) return false;
            for (int station = 0; station <= 5; station++)
            {
                Vector3 center = shelterPosition + along * Mathf.Lerp(-5.10f, .60f, station / 5f);
                for (int edge = -1; edge <= 1; edge++)
                {
                    bool supported = TryHeight(center + across * (.78f * edge), out float ground);
                    // Roof edges can overhang the verge. The centre line carries
                    // the two supporting posts and must actually meet the ground.
                    if (supported && ground > datum + .26f ||
                        edge == 0 && (!supported || ground < datum - .32f)) return false;
                }
            }
            return true;
        }

        internal bool TryHeight(Vector3 position, out float top)
        {
            var point = new Vector2(position.x, position.z);
            if (!sampledHeights.TryGetValue(point, out top))
            {
                bool street = paving.TrySampleTop(point, out top, out _);
                bool terrain = CityTerrainSurfacePlan.TrySampleGroundTop(layout, point, out float soil, out _);
                for (int i = 0; i < hillsideGround.Count; i++)
                {
                    if (!hillsideGround[i].TryHeight(point, out float hillside)) continue;
                    soil = terrain ? Mathf.Max(hillside, soil) : hillside;
                    terrain = true;
                }
                top = street && terrain ? Mathf.Max(top, soil) : street ? top : terrain ? soil : float.NaN;
                // The index lasts for the layout; retain only a bounded set of repeated samples.
                if (sampledHeights.Count < 8192) sampledHeights.Add(point, top);
            }
            return !float.IsNaN(top);
        }

        internal bool TryQueueGround(Vector3 curb, Vector3 slot, out float top)
        {
            top = curb.y;
            if (!IsClear(Envelope(curb, slot, CityPedestrianPlanner.AgentRadius))) return false;
            // The short branch can cross a curb or a verge. Check the same built
            // ground as the shelter, including the real graded hillside triangles.
            return TryHeight((curb + slot) * .5f, out float middle) && Mathf.Abs(middle - curb.y) <= .25f &&
                TryHeight(slot, out top) && Mathf.Abs(top - middle) <= .25f;
        }

        internal static Rect ShelterReservation(Vector3 shelter, Vector3 forward, Vector3 towardRoad)
        {
            Rect roof = CityBusStopWorldBuilder.DescribeFurnitureFootprint(shelter, forward, towardRoad);
            // The overhang reaches 4.875 m aft and .59 m from its centreline.
            // Leave a further 36 cm around it at posts, walls and planted edges.
            const float margin = .36f;
            return Rect.MinMaxRect(roof.xMin - margin, roof.yMin - margin, roof.xMax + margin, roof.yMax + margin);
        }

        internal static Rect PassengerAccess(Vector3 lane, Vector3 forward, Vector3 shelter, Vector3 towardRoad)
        {
            forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            towardRoad = Vector3.ProjectOnPlane(towardRoad, Vector3.up).normalized;
            // Span both door docks, the waiting area and the walking strip beyond
            // the open doors. Expand for a standing passenger's body at either end.
            Vector3 inner = lane - towardRoad * (CityBusDesignVehicle.Default.BodyWidth * .5f +
                CityBusRidePlan.DoorBodyClearance);
            Vector3 outer = shelter + towardRoad * .5f;
            // Include the whole open front of the shelter, including the bench.
            // Its back and the pole stay solid and outside this standing area.
            const float rear = CityBusStopWorldBuilder.ShelterOffsetAlongLane - 2.33f;
            Rect pavement = Envelope(inner + forward * rear, inner + forward * 4.15f, .35f);
            Rect waiting = Envelope(outer + forward * rear, outer + forward * 4.15f, .35f);
            return Rect.MinMaxRect(Mathf.Min(pavement.xMin, waiting.xMin), Mathf.Min(pavement.yMin, waiting.yMin),
                Mathf.Max(pavement.xMax, waiting.xMax), Mathf.Max(pavement.yMax, waiting.yMax));
        }

        internal static Rect PavementApproach(Vector3 lane, Vector3 forward, Vector3 towardRoad)
        {
            Vector3 center = lane - Vector3.ProjectOnPlane(towardRoad, Vector3.up).normalized * PavementOffsetFromLane;
            Vector3 reach = Vector3.ProjectOnPlane(forward, Vector3.up).normalized * PavementApproachLength;
            return Envelope(center - reach, center + reach, CityPedestrianPlanner.AgentRadius);
        }

        private void Add(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Vector2Int min = Cell(rect.min), max = Cell(rect.max);
            for (int x = min.x; x <= max.x; x++)
            for (int z = min.y; z <= max.y; z++)
            {
                var key = new Vector2Int(x, z);
                if (!cells.TryGetValue(key, out var bucket)) cells.Add(key, bucket = new List<Rect>());
                bucket.Add(rect);
            }
        }

        private bool IsClear(Rect rect)
        {
            Vector2Int min = Cell(rect.min), max = Cell(rect.max);
            for (int x = min.x; x <= max.x; x++)
            for (int z = min.y; z <= max.y; z++)
            {
                if (!cells.TryGetValue(new Vector2Int(x, z), out var bucket)) continue;
                for (int i = 0; i < bucket.Count; i++)
                    if (bucket[i].Overlaps(rect)) return false;
            }
            return true;
        }

        private static Rect Xz(Bounds box) =>
            Rect.MinMaxRect(box.min.x, box.min.z, box.max.x, box.max.z);

        private static Rect Envelope(Vector3 start, Vector3 end, float radius) =>
            Rect.MinMaxRect(Mathf.Min(start.x, end.x) - radius, Mathf.Min(start.z, end.z) - radius,
                Mathf.Max(start.x, end.x) + radius, Mathf.Max(start.z, end.z) + radius);

        private static Vector2Int Cell(Vector2 point) =>
            new Vector2Int(Mathf.FloorToInt(point.x / CellSize), Mathf.FloorToInt(point.y / CellSize));
    }
}
