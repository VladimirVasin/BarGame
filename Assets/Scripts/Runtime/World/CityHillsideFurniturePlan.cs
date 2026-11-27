using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A small level part of the existing soil, blended into its surrounding bank.</summary>
    public readonly struct CityHillsideGroundPad
    {
        public Rect Bounds { get; }
        public float TopY { get; }
        public float BlendDistance => 1.5f;
        public CityHillsideGroundPad(Rect bounds, float top) { Bounds = bounds; TopY = top; }
    }

    /// <summary>A measured soil approach from street level to a flat furniture landing.</summary>
    public readonly struct CityHillsideGroundRamp
    {
        public Rect Bounds { get; }
        public float StartZ { get; }
        public float EndZ { get; }
        public float StartY { get; }
        public float EndY { get; }
        public float BlendDistance => 1.5f;
        public CityHillsideGroundRamp(Rect bounds, float startZ, float endZ, float startY, float endY)
        { Bounds = bounds; StartZ = startZ; EndZ = endZ; StartY = startY; EndY = endY; }
    }

    public readonly struct CityHillsideFurniturePlacement
    {
        public string Id { get; }
        public CityMiscKind Kind { get; }
        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public Vector3 ViewTarget { get; }
        public Quaternion Rotation => Quaternion.LookRotation(Forward);
        public Rect Bounds { get; }
        public Rect AccessBounds { get; }
        public int Variant { get; }
        internal CityHillsideFurniturePlacement(string id, CityMiscKind kind, Vector3 position,
            Vector3 forward, Vector3 target, Rect bounds, Rect access, int variant)
        { Id = id; Kind = kind; Position = position; Forward = forward; ViewTarget = target;
            Bounds = bounds; AccessBounds = access; Variant = variant; }
    }

    /// <summary>Finite scenery stops on the measured hill walks, separate from the old grid frontage.</summary>
    public sealed class CityHillsideFurniturePlan
    {
        public const string HomeBenchId = "ridge-home-view-bench";
        public const string LoftPhoneId = "ridge-West-RoadsidePhoneBooth-09";
        public const float BenchSpacing = 52f, DumpsterSpacing = 58f, PhoneBoothSpacing = 82f;
        public const int MaximumPlacements = 96;
        internal const int InfillDumpstersPerBank = 3;
        private static readonly ConditionalWeakTable<CityLayout, RawPlan> RawPlans = new();
        private static readonly ConditionalWeakTable<CityLayout, CityHillsideFurniturePlan> Plans = new();
        public IReadOnlyList<CityHillsideFurniturePlacement> Placements { get; }
        public IReadOnlyList<CityBenchSeat> Seats { get; }
        public IReadOnlyList<CityStreetUtilityDock> UtilityDocks { get; }
        public IReadOnlyList<Rect> SolidFootprints { get; }
        public IReadOnlyList<Vector2[]> SolidPolygons { get; }
        internal IReadOnlyList<Rect> BusReservations { get; }

        public static CityHillsideFurniturePlan ForLayout(CityLayout layout) => Plans.GetValue(layout, l => new(l));
        // Raw pads use only the analytic ground recipe. Grid construction may call this without
        // recursing into its own cache; final contacts below are sampled on the built triangles.
        public static IReadOnlyList<CityHillsideGroundPad> GroundPads(CityLayout layout) =>
            RawPlans.GetValue(layout, l => new(l)).Pads;
        public static IReadOnlyList<CityHillsideGroundRamp> GroundRamps(CityLayout layout) =>
            RawPlans.GetValue(layout, l => new(l)).Ramps;

        private CityHillsideFurniturePlan(CityLayout layout)
        {
            var placements = new List<CityHillsideFurniturePlacement>();
            var seats = new List<CityBenchSeat>();
            var docks = new List<CityStreetUtilityDock>();
            var solids = new List<Rect>();
            var polygons = new List<Vector2[]>();
            var dockHeights = new Dictionary<string, float>();
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            // Service routing samples the finished terrain; it must remain downstream of raw
            // pad construction. Reject its exact published reservations only at this stage.
            var busReservations = new List<Rect>(CityBusNetworkFurniture.Reservations(
                CityDistrictBusPlanner.CreateRoadRouting(layout), layout));
            BusReservations = busReservations.AsReadOnly();
            var raw = RawPlans.GetValue(layout, l => new(l));
            RidgeKioskSitePlan.TryCreate(layout, out var kiosk);
            RidgeKioskCourtPlan court = kiosk?.Court;
            string courtPhone = null;
            float nearestPhone = float.PositiveInfinity;
            if (court != null)
                foreach (var item in raw.Sites)
                {
                    if (item.Kind != CityMiscKind.RoadsidePhoneBooth || item.Id == LoftPhoneId) continue;
                    float distance = (item.Position - kiosk.Origin).sqrMagnitude;
                    if (distance >= nearestPhone) continue;
                    nearestPhone = distance;
                    courtPhone = item.Id;
                }
            string homeFailure = "raw candidate missing: " + raw.HomeFailure;
            // The original surveyed pads are part of the terrain recipe (including viewpoints).
            // Only the two public destinations retain phones. Household bins use the actual houses.
            var candidates = new List<CityHillsideFurniturePlacement>();
            foreach (var prior in raw.Sites)
                if (prior.Kind == CityMiscKind.ParkBench || prior.Id == courtPhone || prior.Id == LoftPhoneId)
                    candidates.Add(prior);
            var housing = CityHillsideNeighborhoodPlan.ForLayout(layout);
            foreach (var house in housing.AllBuildings)
            {
                Vector3 curb = house.StreetContactWorld;
                float side = Mathf.Sign(house.DoorWorld.z-curb.z);
                Vector3 front = Vector3.back*side;
                foreach (float lateral in new[] { 2.75f,-2.75f,4.25f,-4.25f })
                {
                    Vector2 p = new(curb.x+lateral,curb.z+side*2.65f);
                    var bounds = RotatedBounds(p,front,new Vector2(1.04f,.64f),Vector2.zero);
                    var access = RotatedBounds(p,front,new Vector2(1.04f,1.15f),new Vector2(0f,.42f));
                    Vector3 path = new(p.x,curb.y,curb.z);
                    candidates.Add(new CityHillsideFurniturePlacement($"ridge-house-{house.Lot.Cell.x}-{house.Lot.Cell.y}-bin",
                        CityMiscKind.RoadsideDumpsterAndUtility,new Vector3(p.x,curb.y,p.y),front,path,bounds,access,0));
                }
            }
            foreach (var item in candidates)
            {
                bool onCourt = item.Id == courtPhone;
                var candidate = onCourt ? new CityHillsideFurniturePlacement(item.Id, item.Kind, court.Phone,
                    Vector3.right, court.Phone + Vector3.right * 3f, court.PhoneBounds, court.PhoneAccess, item.Variant) : item;
                Vector2 point = new(candidate.Position.x, candidate.Position.z);
                float top = candidate.Position.y;
                if (!onCourt && !ground.TrySampleTop(point, out top))
                { if (candidate.Id == HomeBenchId) homeFailure = "no ground at " + point; continue; }
                bool clear = !(RidgeParkingPlan.ForLayout(layout)?.SurfaceBounds.Overlaps(candidate.AccessBounds) ?? false);
                // The public phone has a narrow graded connection all the way
                // to the footway. Its whole strip must survive final reservations.
                Rect reservedAccess = candidate.Id == LoftPhoneId ? raw.LoftPhoneApproach : candidate.AccessBounds;
                foreach (Rect area in busReservations)
                    if (reservedAccess.Overlaps(area))
                    { clear = false; if (candidate.Id == HomeBenchId) homeFailure = "bus reservation " + area; break; }
                foreach (var site in CityHillsideSiteDetails.ForLayout(layout))
                    if (!CityHillsideSiteDetails.IsClearedForCourt(site, court) &&
                        (candidate.Id == LoftPhoneId ? reservedAccess : candidate.Bounds).Overlaps(site.Bounds))
                    { clear = false; if (candidate.Id == HomeBenchId) homeFailure = site.Part + " at " + site.Origin + " bounds " + site.Bounds; break; }
                if (!onCourt && kiosk != null &&
                    (candidate.AccessBounds.Overlaps(kiosk.Bounds) || candidate.AccessBounds.Overlaps(kiosk.Apron) ||
                        court != null && court.Overlaps(candidate.AccessBounds)))
                { clear = false; if (candidate.Id == HomeBenchId) homeFailure = "kiosk footprint"; }
                if (!onCourt)
                    foreach (Vector2 corner in Corners(candidate.AccessBounds))
                        if (!ground.TrySampleTop(corner, out float y) || Mathf.Abs(y - top) > .085f)
                        { clear = false; if (candidate.Id == HomeBenchId) homeFailure = $"ground corner {corner}: {y:F4} versus {top:F4}"; }
                if (candidate.Kind == CityMiscKind.RoadsideDumpsterAndUtility)
                {
                    if (!clear || solids.Exists(r => r.Overlaps(candidate.AccessBounds)) ||
                        placements.Exists(p => p.Kind == candidate.Kind &&
                            Mathf.Abs(p.Position.y-top)<.8f && Vector3.Distance(p.ViewTarget,candidate.ViewTarget)<43f))
                        continue;
                    // The collection lane ends on this house's own street, on this level.
                    Vector2 curb = new(candidate.ViewTarget.x,candidate.ViewTarget.z);
                    var lane = CityStreetUtilitySites.Strip(point,curb,.94f);
                    foreach (Rect area in busReservations) if (area.Overlaps(lane)) clear = false;
                    foreach (var detail in CityHillsideSiteDetails.ForLayout(layout))
                        if (detail.Bounds.Overlaps(lane)) clear = false;
                    foreach (var house in housing.AllBuildings)
                        if (house.FoundationBounds.Overlaps(lane) || house.ApproachBounds.Overlaps(lane)) clear = false;
                    if (Mathf.Abs(top-candidate.ViewTarget.y)>.27f) clear = false;
                    for (int i=0; clear && i<7; i++)
                    {
                        Vector2 sample = Vector2.Lerp(point,curb,i/8f);
                        if (!ground.TrySampleTop(sample,out float level) || Mathf.Abs(top-level)>.24f) clear=false;
                    }
                }
                if (!clear) continue;
                // The two benches share the new straight terrace frontage.
                // Keep the surveyed earth/reservation recipe unchanged: its
                // larger level pads also contain these smaller aligned docks.
                bool straightBench=candidate.Id==CityPawnshopBenchTerracePlan.LeftBenchId ||
                    candidate.Id==CityPawnshopBenchTerracePlan.RightBenchId;
                Vector3 forward=straightBench?Vector3.forward:candidate.Forward;
                Rect body=straightBench?RotatedBounds(point,forward,new Vector2(1.18f,.40f),Vector2.zero):candidate.Bounds;
                Rect access=straightBench?RotatedBounds(point,forward,new Vector2(1.88f,1.35f),new Vector2(0f,.55f)):candidate.AccessBounds;
                Vector3 target=straightBench?new Vector3(point.x,candidate.ViewTarget.y,point.y+55f):candidate.ViewTarget;
                var placed = new CityHillsideFurniturePlacement(candidate.Id, candidate.Kind,
                    new Vector3(point.x, top, point.y), forward, target,body,access,candidate.Variant);
                placements.Add(placed);
                solids.Add(placed.Bounds);
            }
            FillRoadsideGaps();
            foreach (var placed in placements) Register(placed);
            if (layout.HasSummitHome && !seats.Exists(s => s.Id == HomeBenchId))
                throw new InvalidOperationException("The Ridge home viewpoint needs a supported, unobstructed bench: " + homeFailure);
            if (layout.HasSummitHome && !placements.Exists(p => p.Id == LoftPhoneId))
                throw new InvalidOperationException("The pigeon courtyard phone needs its supported public landing.");
            Placements = placements.AsReadOnly(); Seats = seats.AsReadOnly();
            UtilityDocks = docks.AsReadOnly(); SolidFootprints = solids.AsReadOnly(); SolidPolygons = polygons.AsReadOnly();

            void Register(CityHillsideFurniturePlacement placed)
            {
                Vector2 point = new(placed.Position.x, placed.Position.z);
                bool phone = placed.Kind == CityMiscKind.RoadsidePhoneBooth;
                bool bench = placed.Kind == CityMiscKind.ParkBench;
                if (phone)
                {
                    Vector2 side = new(placed.Forward.z, -placed.Forward.x);
                    foreach (float x in new[] { -.64f, .64f })
                        polygons.Add(Rectangle(point + side*x, placed.Forward, new Vector2(.0375f,.53f)));
                    polygons.Add(Rectangle(point - new Vector2(placed.Forward.x, placed.Forward.z)*.53f,
                        placed.Forward, new Vector2(.67f,.033f)));
                }
                else polygons.Add(Rectangle(point, placed.Forward, bench ? new Vector2(1.15f, .36f) : new Vector2(.89f, .48f)));
                if (bench)
                    seats.Add(new CityBenchSeat(placed.Id, placed.Position + Vector3.up * CityBenchSitPlan.ParkSeatTopHeight,
                        CityBenchSitPlan.ParkSeatWidth, CityBenchSitPlan.ParkSeatDepth, placed.Position.y, placed.Forward));
                else
                {
                    Vector3 approach = placed.Id == courtPhone ? court.Point(-2.55f,-2.43f) :
                        phone ? new Vector3(placed.Position.x,placed.ViewTarget.y,raw.LoftPhoneApproach.yMax+.32f) : placed.ViewTarget;
                    var dock = CityStreetUtilityDock.At(placed.Id, phone, placed.Position, placed.Forward, approach);
                    if (dockHeights.TryGetValue(placed.Id, out float height))
                    {
                        Vector3 stand = dock.StandPosition;
                        stand.y = height;
                        dock = new CityStreetUtilityDock(dock.Id, dock.DecorationId, dock.Kind, stand, dock.Facing, approach);
                    }
                    docks.Add(dock);
                }
            }

            void FillRoadsideGaps()
            {
                const CityMiscKind dumpster = CityMiscKind.RoadsideDumpsterAndUtility;
                var roads = CityHillsideStreetPlan.ForLayout(layout).Routes;
                if (roads.Count == 0) return;
                var reserved = new List<Rect>(busReservations);
                foreach (var placement in placements) reserved.Add(placement.AccessBounds);
                foreach (var house in housing.AllBuildings)
                {
                    reserved.Add(house.FoundationBounds);
                    reserved.Add(house.ApproachBounds);
                    reserved.Add(house.CourtBounds);
                }
                foreach (var detail in CityHillsideSiteDetails.ForLayout(layout)) reserved.Add(detail.Bounds);
                foreach (var stair in CityHillsideAccessPlan.ForLayout(layout).AllStairs)
                    foreach (Rect bounds in stair.WalkableBounds) reserved.Add(Expand(bounds, .55f));
                if (RidgeParkingPlan.ForLayout(layout) is { } parking) reserved.Add(parking.SurfaceBounds);
                if (RidgeMotelPlan.ForLayout(layout) is { } motel) reserved.Add(motel.Court);
                if (RidgeGaragePlan.ForLayout(layout) is { } garage) reserved.Add(garage.Bounds);
                if (kiosk != null)
                {
                    reserved.Add(kiosk.Bounds);
                    reserved.Add(kiosk.Apron);
                }

                // Survey the existing verges of the intermediate straight streets. The end
                // streets serve the coast and summit destinations, and the bends carry queues.
                foreach (var road in roads)
                {
                    int count = 0;
                    foreach (int lane in new[] { 1, 3, 5, 2, 4 })
                    {
                        if (count == InfillDumpstersPerBank || placements.Count == MaximumPlacements) break;
                        if (TryFillLane(road, lane)) count++;
                    }
                }

                FillHouseGaps(useFootwayEdge: false);
                FillHouseGaps(useFootwayEdge: true);
                MoveToHouseLines();

                void MoveToHouseLines()
                {
                    foreach (var (left, right) in HouseNeighbours(housing))
                    {
                        int index = placements.FindIndex(item => item.Kind == dumpster && IsBetweenHouses(item, left, right));
                        if (index < 0) continue;
                        var bin = placements[index];
                        float side = -bin.Forward.z;
                        var house = MatchesStreet(left) ? left : right;
                        var other = house == left ? right : left;
                        float facadeZ = HouseLineZ(house);
                        if (MatchesStreet(other) && Mathf.Abs(other.PavementEdgeZ - house.PavementEdgeZ) < .1f)
                            facadeZ = side * Mathf.Max(side * facadeZ, side * HouseLineZ(other));
                        float gapMin = left.FoundationBounds.xMax;
                        float gapMax = right.FoundationBounds.xMin;
                        const float footingClearance = 1.12f;
                        const float clearPassage = 1.05f;
                        float min = gapMin + footingClearance;
                        float max = gapMax - footingClearance;
                        bool canFaceStreet = gapMax - gapMin >= footingClearance + 1.03f + clearPassage;
                        float preferred = canFaceStreet ? Mathf.Clamp(bin.Position.x, min, max) : bin.Position.x;
                        var offsets = new[] { preferred, min, max, Mathf.Lerp(min, max, .25f), Mathf.Lerp(min, max, .75f) };
                        var road = roads.First(route => route.Bank == house.Bank);
                        reserved.Remove(bin.AccessBounds);
                        bool moved = false;
                        // Try the facade before accepting a small correction for terrain.
                        foreach (float depth in new[] { 0f, -.2f, .2f, -.4f })
                        {
                            moved = TryAcrossGap(facadeZ + side * depth);
                            if (moved) break;
                        }
                        if (!moved)
                        {
                            float edge = house.PavementEdgeZ;
                            float far = (facadeZ - edge) * side;
                            float near = Mathf.Max(.08f, (bin.Position.z - edge) * side - .48f + .35f);
                            if (far >= near)
                            {
                                int intervals = Mathf.Max(1, Mathf.CeilToInt((far - near) / .28f));
                                for (int step = 0; step <= intervals; step++)
                                {
                                    float front = edge + side * Mathf.Lerp(far, near, (float)step / intervals);
                                    moved = TryAlongWall(front) || TryAcrossGap(front);
                                    if (moved) break;
                                }
                            }
                        }
                        if (!moved) reserved.Add(bin.AccessBounds);

                        bool TryAcrossGap(float front)
                        {
                            if (!canFaceStreet) return false;
                            foreach (float x in offsets)
                                if (Mathf.Max(x - gapMin, gapMax - x) - 1.03f >= clearPassage &&
                                    TryCommit(x, front + side * .48f, bin.Forward)) return true;
                            return false;
                        }

                        bool TryAlongWall(float front)
                        {
                            // The footing reaches 62.5 cm behind the bin. The gap beside
                            // the long wall stays clear of the hull and its open lid.
                            const float wallOffset = .76f;
                            if (gapMax - gapMin < wallOffset + .52f + clearPassage) return false;
                            float z = front + side * .92f;
                            bool nearLeft = bin.Position.x - gapMin <= gapMax - bin.Position.x;
                            return nearLeft
                                ? TryCommit(gapMin + wallOffset, z, Vector3.right) || TryCommit(gapMax - wallOffset, z, Vector3.left)
                                : TryCommit(gapMax - wallOffset, z, Vector3.left) || TryCommit(gapMin + wallOffset, z, Vector3.right);
                        }

                        bool TryCommit(float x, float z, Vector3 facing)
                        {
                            if (!TryHouseLine(road, bin, x, z, facing, out var placement, out float standY)) return false;
                            placements[index] = placement;
                            solids[index] = placement.Bounds;
                            dockHeights[bin.Id] = standY;
                            reserved.Add(placement.AccessBounds);
                            return true;
                        }

                        bool MatchesStreet(CityHillsideBuilding candidate)
                        {
                            float setback = (bin.Position.z - candidate.PavementEdgeZ) * side;
                            return StreetSide(candidate) == side && setback > 0f && setback < 2.7f;
                        }
                    }
                }

                bool TryHouseLine(CityHillsideStreetRoute road, CityHillsideFurniturePlacement bin,
                    float x, float z, Vector3 forward, out CityHillsideFurniturePlacement placement, out float standY)
                {
                    placement = default;
                    standY = 0f;
                    Vector2 center = new(x, z);
                    Vector2 stand = center + new Vector2(forward.x, forward.z) * CityStreetUtilityModel.BinStand;
                    Vector2 start = new(stand.x, bin.ViewTarget.z);
                    Rect body = RotatedBounds(center, forward, new Vector2(.89f, .48f), Vector2.zero);
                    var footing = RotatedBounds(center, forward, new Vector2(1.03f, .775f), new Vector2(0f, .15f));
                    var lane = CityStreetUtilitySites.Strip(start, stand, .43f);
                    var access = Rect.MinMaxRect(Mathf.Min(footing.xMin, lane.xMin), Mathf.Min(footing.yMin, lane.yMin),
                        Mathf.Max(footing.xMax, lane.xMax), Mathf.Max(footing.yMax, lane.yMax));
                    if (reserved.Exists(area => area.Overlaps(footing) || area.Overlaps(lane)) || court?.Overlaps(access) == true ||
                        !road.TrySampleTop(start, out float curbY, out var surface) || surface != CityStreetSupportKind.Sidewalk ||
                        !ground.TrySampleTop(stand, out standY)) return false;
                    foreach (Vector2 corner in Corners(body))
                        if (road.TrySampleTop(corner, out _, out _)) return false;

                    float low = float.PositiveInfinity, high = float.NegativeInfinity;
                    foreach (Vector2 corner in Corners(footing))
                    {
                        if (!road.TrySampleTop(corner, out float y, out _) &&
                            !(ground.Contains(corner) && ground.TrySampleTop(corner, out y))) return false;
                        low = Mathf.Min(low, y);
                        high = Mathf.Max(high, y);
                    }
                    float top = Mathf.Min(low + .135f, standY + .09f);
                    if (high - top > .175f || Mathf.Abs(standY - top) > .135f) return false;

                    // Probe the existing pedestrian corridor across the paving seam on
                    // both sides of the capsule, including the surface the player stands on.
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(start, stand) / .22f));
                    for (int row = -1; row <= 1; row++)
                    {
                        float previous = curbY;
                        for (int step = 0; step <= steps; step++)
                        {
                            Vector2 sample = Vector2.Lerp(start, stand, (float)step / steps) + Vector2.right * (row * .32f);
                            if (!road.TrySampleTop(sample, out float y, out _) &&
                                !(ground.Contains(sample) && ground.TrySampleTop(sample, out y))) return false;
                            if (Mathf.Abs(y - previous) > .16f) return false;
                            previous = y;
                        }
                    }
                    placement = new CityHillsideFurniturePlacement(bin.Id, dumpster, new Vector3(x, top, z),
                        forward, new Vector3(start.x, curbY, start.y), body, access, 0);
                    return true;
                }

                void FillHouseGaps(bool useFootwayEdge)
                {
                    // A middle house can open onto the next street even when it
                    // shares its parcel row with houses facing the lower street.
                    foreach (var (left, right) in HouseNeighbours(housing))
                    {
                        if (placements.Count >= MaximumPlacements) break;
                        float min = left.FoundationBounds.xMax + 1.13f;
                        float max = right.FoundationBounds.xMin - 1.13f;
                        if (min > max || placements.Exists(bin => bin.Kind == dumpster && IsBetweenHouses(bin, left, right))) continue;
                        var road = roads.First(route => route.Bank == left.Bank);
                        string id = $"ridge-house-gap-{left.Lot.Cell.x}-{left.Lot.Cell.y}-{right.Lot.Cell.x}-bin";
                        float middle = (min + max) * .5f;
                        int steps = Mathf.CeilToInt((max - min) * .5f);
                        for (int step = 0; step <= steps * 2; step++)
                        {
                            float x = Mathf.Clamp(middle + ((step + 1) / 2) * (step % 2 == 0 ? -1f : 1f), min, max);
                            if (TryHouseFrontage(road, left, id, x, useFootwayEdge) ||
                                left.PavementEdgeZ != right.PavementEdgeZ && TryHouseFrontage(road, right, id, x, useFootwayEdge)) break;
                        }
                    }
                }

                bool TryHouseFrontage(CityHillsideStreetRoute road, CityHillsideBuilding house, string id, float x, bool useFootwayEdge)
                {
                    float side = Mathf.Sign(house.PavementEdgeZ - house.StreetContactWorld.z);
                    float edge = house.PavementEdgeZ;
                    // Recover the actual kerb on the widened ends of a shelf.
                    float inner = -.85f, outer = 1.35f;
                    if (!IsFootway(inner)) return false;
                    for (int probe = 0; probe < 10; probe++)
                    {
                        float middle = (inner + outer) * .5f;
                        if (IsFootway(middle)) inner = middle;
                        else outer = middle;
                    }
                    float kerb = edge + outer * side;
                    if (!useFootwayEdge) return TryPlaceBin(road, id, x, kerb, side);
                    return TryPlaceBin(road, id, x, kerb, side, setback: .24f) ||
                        TryPlaceBin(road, id, x, kerb, side, setback: .07f);

                    bool IsFootway(float offset) => road.TrySampleTop(new Vector2(x, edge + offset * side),
                        out _, out var material) && material == CityStreetSupportKind.Sidewalk;
                }

                bool TryFillLane(CityHillsideStreetRoute road, int lane)
                {
                    float z = road.GetLaneZ(lane);
                    float preferred = (road.LaneMinX + road.LaneMaxX) * .5f + (lane % 3 - 1) * 14f;
                    for (int attempt = 0; attempt < 15; attempt++)
                    {
                        float x = preferred + ((attempt + 1) / 2) * (attempt % 2 == 0 ? -5f : 5f);
                        if (x < road.LaneMinX + 16f || x > road.LaneMaxX - 16f) continue;
                        for (int side = -1; side <= 1; side += 2)
                        {
                            string id = $"ridge-{road.Bank}-lane-{lane}-bin-infill";
                            float edge = z + side * CityHillsideStreetRoute.OuterHalfWidth;
                            if (TryPlaceBin(road, id, x, edge, side, spaced: true)) return true;
                        }
                    }
                    return false;
                }

                bool TryPlaceBin(CityHillsideStreetRoute road, string id, float x, float edge, float side,
                    bool spaced = false, float setback = .54f)
                {
                    bool useFootwayEdge = setback < .48f;
                    Vector2 point = new(x, edge + side * setback);
                    Vector2 curb = new(x, edge - side * 1.25f);
                    Vector2 standPoint = point - Vector2.up * (side * CityStreetUtilityModel.BinStand);
                    Vector3 forward = Vector3.back * side;
                    Rect body = RotatedBounds(point, forward, new Vector2(.89f, .48f), Vector2.zero);
                    Rect footing = RotatedBounds(point, forward, new Vector2(1.03f, .775f), new Vector2(0f, .15f));
                    Rect access = CityStreetUtilitySites.Strip(point + Vector2.up * side * .625f, curb, 1.13f);
                    Rect approachLane = CityStreetUtilitySites.Strip(standPoint, curb - Vector2.up * side * .36f, .48f);
                    if (reserved.Exists(rect => useFootwayEdge ? rect.Overlaps(footing) || rect.Overlaps(approachLane) : rect.Overlaps(access)) ||
                        court?.Overlaps(access) == true ||
                        !road.TrySampleTop(curb, out float curbY, out var surface) || surface != CityStreetSupportKind.Sidewalk ||
                        !road.TrySampleTop(standPoint, out float standY, out var standSurface) || standSurface != CityStreetSupportKind.Sidewalk ||
                        spaced && placements.Exists(p => p.Kind == dumpster && Mathf.Abs(p.Position.y - standY) < .8f &&
                            Mathf.Abs(p.ViewTarget.z - curb.y) < 11f && Mathf.Abs(p.ViewTarget.x - x) < 30f)) return false;
                    if (useFootwayEdge)
                    {
                        // Keep a 1.5 m through strip beside the hull. Verify the
                        // width at both ends against the actual paved section.
                        float innerEdge = edge - side * (1.5f + .48f - setback);
                        foreach (float along in new[] { body.xMin, body.xMax })
                            if (!road.TrySampleTop(new Vector2(along, innerEdge), out _, out var inner) ||
                                inner != CityStreetSupportKind.Sidewalk) return false;
                    }
                    else if (!spaced)
                        foreach (Vector2 corner in Corners(body))
                            if (road.TrySampleTop(corner, out _, out _)) return false;

                    // The model's concrete footing straddles the seam. Set the hull beyond
                    // the footway and keep the interaction dock on the existing paving.
                    float lowest = standY, highest = standY;
                    foreach (Vector2 corner in Corners(footing))
                    {
                        if (!road.TrySampleTop(corner, out float level, out _) && !ground.TrySampleTop(corner, out level)) return false;
                        lowest = Mathf.Min(lowest, level);
                        highest = Mathf.Max(highest, level);
                    }
                    float top = Mathf.Min(lowest + .135f, standY);
                    float maxRise = useFootwayEdge ? .205f : .155f;
                    float maxSink = useFootwayEdge ? .135f : .105f;
                    if (highest - top > maxRise || standY - top > maxSink) return false;

                    placements.Add(new CityHillsideFurniturePlacement(id, dumpster, new Vector3(x, top, point.y), forward,
                        new Vector3(x, curbY, curb.y), body, access, 0));
                    solids.Add(body);
                    dockHeights.Add(id, standY);
                    reserved.Add(access);
                    return true;
                }
            }
        }

        internal static IEnumerable<(CityHillsideBuilding Left, CityHillsideBuilding Right)> HouseNeighbours(
            CityHillsideNeighborhoodPlan housing)
        {
            foreach (var row in housing.Buildings.GroupBy(house => (house.Bank, house.Lot.Cell.y)))
            {
                CityHillsideBuilding previous = null;
                foreach (var house in row.OrderBy(house => house.FoundationBounds.center.x))
                {
                    if (previous != null) yield return (previous, house);
                    previous = house;
                }
            }
        }

        internal static bool IsBetweenHouses(CityHillsideFurniturePlacement bin, CityHillsideBuilding left, CityHillsideBuilding right)
        {
            return bin.Bounds.xMin >= left.FoundationBounds.xMax && bin.Bounds.xMax <= right.FoundationBounds.xMin &&
                (AtFrontage(left) || AtFrontage(right));

            bool AtFrontage(CityHillsideBuilding house)
            {
                float side = StreetSide(house);
                float setback = (bin.Position.z - house.PavementEdgeZ) * side;
                float approach = (house.PavementEdgeZ - bin.ViewTarget.z) * side;
                float limit = Mathf.Max(2.7f, (HouseLineZ(house) - house.PavementEdgeZ) * side + 1.4f);
                return approach > -.15f && approach < 2.7f && setback > 0f && setback < limit;
            }
        }

        private static float StreetSide(CityHillsideBuilding house) => Mathf.Sign(house.PavementEdgeZ - house.StreetContactWorld.z);
        internal static float HouseLineZ(CityHillsideBuilding house) => StreetSide(house) > 0f
            ? house.FoundationBounds.yMin : house.FoundationBounds.yMax;

        private static IEnumerable<Vector2> Corners(Rect r)
        {
            yield return r.min; yield return r.max;
            yield return new Vector2(r.xMin, r.yMax); yield return new Vector2(r.xMax, r.yMin);
        }

        private sealed class RawPlan
        {
            internal readonly List<CityHillsideFurniturePlacement> Sites = new();
            internal readonly List<CityHillsideGroundPad> Pads = new();
            internal readonly List<CityHillsideGroundRamp> Ramps = new();
            internal string HomeFailure { get; private set; } = "not surveyed";
            internal Rect LoftPhoneApproach { get; private set; }
            private readonly CityLayout layout;
            private readonly List<Rect> reserved = new();
            private readonly IReadOnlyList<CityHillsideStreetRoute> roads;
            private readonly CityHillsideQuarterPlan quarter;
            private readonly CityHillsideNeighborhoodPlan housing;
            private readonly List<Vector2[]> streetKeepouts = new();

            internal RawPlan(CityLayout layout)
            {
                this.layout = layout;
                roads = CityHillsideStreetPlan.ForLayout(layout).Routes;
                quarter = CityHillsideQuarterPlan.ForLayout(layout);
                housing = CityHillsideNeighborhoodPlan.ForLayout(layout);
                if (quarter.Links.Count == 0) return;
                if (RidgeMotelPlan.ForLayout(layout) is { } motel) reserved.Add(motel.Court);
                if (RidgeGaragePlan.ForLayout(layout) is { } garage) reserved.Add(garage.Bounds);
                foreach (var house in housing.AllBuildings)
                { reserved.Add(Expand(house.FoundationBounds, .65f)); reserved.Add(Expand(house.ApproachBounds, .45f));
                    reserved.Add(Expand(house.CourtBounds, .45f)); }
                foreach (var stair in CityHillsideAccessPlan.ForLayout(layout).AllStairs)
                    foreach (Rect piece in stair.WalkableBounds) reserved.Add(Expand(piece, .55f));
                // Bus placement consumes GroundGrid, so raw pads cannot request the bus plan.
                // Its shelter/queue envelope is inside 8.5 m of the surveyed road axis. Keep
                // that entire strip unchanged, including where a final stop may be selected.
                foreach (var road in roads) streetKeepouts.AddRange(road.Path.Ribbon(17f));

                if (layout.HasSummitHome)
                {
                    reserved.Add(CityPigeonCourtyardPlan.FurnitureReservation);
                    reserved.Add(CityGlassReturnSitePlan.LandReservation);
                    // Across the street from Home, just west of the stone lip. The bench faces
                    // the lower truss and descending roofs rather than the house or the wall.
                    var target = quarter.Bridges[0].DeckCenterWorld;
                    var upperBridge = quarter.Bridges[1];
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        Vector2 p = new(upperBridge.WestBearingWorld.x - 8f - attempt * 2f,
                            upperBridge.DeckBounds.yMax + 8f);
                        Vector3 look = target - new Vector3(p.x, 0f, p.y); look.y = 0f;
                        if (!Add(HomeBenchId, CityMiscKind.ParkBench, p, look.normalized, target, 0)) continue;
                        var homeBench = Sites[Sites.Count - 1];
                        // A single graded lane crosses the genuine bank and reaches the open
                        // west plank end. Its northern part stays level around the sit dock.
                        // The terrain applies it after every pad, so the pad's short fade cannot
                        // insert an unwalkable face into this measured approach.
                        Rect ramp = Rect.MinMaxRect(homeBench.Position.x - 3.3f, upperBridge.Link.Bounds.yMax,
                            homeBench.Position.x + .2f, homeBench.AccessBounds.yMax);
                        float top = upperBridge.Link.PavementTopAt(ramp.center.x) - .012f;
                        Ramps.Add(new CityHillsideGroundRamp(ramp, ramp.yMin, homeBench.AccessBounds.yMin,
                            top, homeBench.Position.y));
                        break;
                    }
                }
                foreach (var road in roads)
                {
                    Along(road, CityMiscKind.ParkBench, BenchSpacing, 24f);
                    Along(road, CityMiscKind.RoadsideDumpsterAndUtility, DumpsterSpacing, 40f);
                    Along(road, CityMiscKind.RoadsidePhoneBooth, PhoneBoothSpacing, 65f);
                }
            }

            private void Along(CityHillsideStreetRoute road, CityMiscKind kind, float spacing, float start)
            {
                int serial = 0;
                for (float station = start; station < road.Path.Length - 12f && Sites.Count < MaximumPlacements; station += spacing)
                {
                    string id = $"ridge-{road.Bank}-{kind}-{serial++:00}";
                    if (id == LoftPhoneId && layout.HasSummitHome)
                    {
                        AddLoftPhone(road);
                        continue;
                    }
                    bool added = false;
                    // Search a bounded nearby station; a doorway or a queue wins over a nominal interval.
                    for (int attempt = 0; attempt < 9 && !added; attempt++)
                    {
                        float shift = attempt == 0 ? 0f : ((attempt + 1) / 2) * 4f * (attempt % 2 == 0 ? -1f : 1f);
                        var sample = road.Path.SampleDistance(station + shift);
                        for (int side = 0; side < 2 && !added; side++)
                        {
                            // North shoulders open towards the town. At a bend use the outside
                            // of the actual road ribbon, never a decorative strip across its footway.
                            Vector2 offset = sample.Right * (side == 0 ? 13f : -13f);
                            Vector2 p = sample.Position + offset;
                            Vector3 facing = kind == CityMiscKind.ParkBench ? new Vector3(.20f * (road.Bank == CityTerraceTransportBank.West ? 1f : -1f), 0f, 1f).normalized
                                : new Vector3(-offset.x, 0f, -offset.y).normalized;
                            if (kind == CityMiscKind.ParkBench && !TryView(p, ref facing)) continue;
                            Vector3 target = new(p.x + facing.x * 55f, road.Profile.SampleDistance(station).Height - 6f, p.y + facing.z * 55f);
                            added = Add(id, kind, p, facing, target, serial & 1);
                        }
                    }
                }
            }

            private void AddLoftPhone(CityHillsideStreetRoute road)
            {
                // Use the same utility ID, light and door dock on the outer south
                // shoulder of the final street. Survey the level landing before
                // construction of the terrain and feather a narrow path to the curb.
                float streetZ = road.GetLaneZ(CityHillsideStreetRoute.LaneCount - 1);
                foreach (float setback in new[] { 13f, 15f, 17f })
                // Start two metres beyond the retaining wall on the west side
                // of the neighbouring frontage. Face north across level earth.
                foreach (float x in new[] { -148f, -150f, -152f, -146f, -144f })
                {
                    Vector2 p = new(x, streetZ - setback);
                    float outside = streetZ - 8f, inside = streetZ;
                    // Find the actual faceted outside edge, including the bus overhang.
                    for (int i = 0; i < 12; i++)
                    {
                        float mid = (outside + inside) * .5f;
                        if (road.TrySampleTop(new Vector2(x, mid), out _, out _)) inside = mid;
                        else outside = mid;
                    }
                    if (!road.TrySampleTop(new Vector2(x, inside), out float curb, out var kind) ||
                        kind != CityStreetSupportKind.Sidewalk) continue;
                    Rect approach = Rect.MinMaxRect(x - .95f, p.y, x + .95f, outside);
                    bool clear = true;
                    foreach (var obstacle in reserved)
                        if (obstacle.Overlaps(approach)) { clear = false; break; }
                    if (!clear || !Add(LoftPhoneId, CityMiscKind.RoadsidePhoneBooth, p, Vector3.forward,
                        new Vector3(x, curb, streetZ), 0)) continue;
                    var phone = Sites[Sites.Count - 1];
                    LoftPhoneApproach = Rect.MinMaxRect(phone.AccessBounds.xMin, phone.AccessBounds.yMin,
                        phone.AccessBounds.xMax, outside);
                    Ramps.Add(new CityHillsideGroundRamp(approach, phone.AccessBounds.yMax, outside,
                        phone.Position.y, curb - .012f));
                    reserved.Add(approach);
                    return;
                }
                throw new InvalidOperationException("No clear outer shoulder for the pigeon courtyard phone.");
            }

            private bool Add(string id, CityMiscKind kind, Vector2 p, Vector3 facing, Vector3 target, int variant)
            {
                Vector2 half = kind == CityMiscKind.ParkBench ? new Vector2(1.18f, .40f)
                    : kind == CityMiscKind.RoadsidePhoneBooth ? new Vector2(.76f, .59f) : new Vector2(2.0f, .65f);
                Rect body = RotatedBounds(p, facing, half, Vector2.zero);
                // A level front lane contains the sit/stand or utility dock plus capsule clearance.
                Rect access = RotatedBounds(p, facing, new Vector2(half.x + .7f, 1.35f), new Vector2(0f, .55f));
                Rect blend = Expand(access, 1.5f);
                if (quarter.TerrainBounds.Contains(p) && blend.xMin < 3f && blend.xMax > -26f) return Reject("warped ravine strip");
                foreach (Rect area in reserved) if (blend.Overlaps(area)) return Reject("reserved " + area);
                foreach (var polygon in streetKeepouts)
                    if (Intersects(blend, polygon)) return Reject("road strip " + CityRoadPolygon.Bounds(polygon));
                foreach (var link in quarter.Links) if (blend.Overlaps(link.Bounds)) return Reject("quarter link " + link.Id);
                foreach (Vector2 corner in Corners(blend))
                {
                    if (quarter.IsExposedRavine(corner, 1.5f)) return Reject("ravine corner " + corner);
                    if (!TryHeight(corner, out _)) return Reject("missing corner " + corner);
                }
                if (!TryHeight(p, out float top)) return Reject("missing centre " + p);
                // Phone booths keep the original city's minimum separation even across adjacent tiers.
                if (kind == CityMiscKind.RoadsidePhoneBooth)
                    foreach (var other in Sites)
                        if (other.Kind == kind && Vector2.Distance(p, new Vector2(other.Position.x, other.Position.z)) < CityDecorationPlanner.PhoneBoothMinimumSpacing) return false;
                var site = new CityHillsideFurniturePlacement(id, kind, new Vector3(p.x, top, p.y), facing,
                    target, body, access, kind == CityMiscKind.ParkBench ? variant : 0);
                Sites.Add(site); Pads.Add(new CityHillsideGroundPad(access, top)); reserved.Add(Expand(access, 1f));
                if (id == HomeBenchId) HomeFailure = "raw accepted " + p;
                return true;
                bool Reject(string reason)
                {
                    if (id == HomeBenchId) HomeFailure = p + ": " + reason;
                    return false;
                }
            }

            private bool TryView(Vector2 p, ref Vector3 facing)
            {
                // Evaluate gaps in the actual building mass, not just the compass bearing. A
                // near wall cannot be the "view". Farther roofs remain part of the panorama.
                float[] bearings = { 0f, 25f, -25f, 48f, -48f, 68f, -68f };
                foreach (float angle in bearings)
                {
                    Vector3 candidate = Quaternion.Euler(0f, angle, 0f) * facing;
                    Vector2 direction = new(candidate.x, candidate.z);
                    bool clear = true;
                    foreach (var building in housing.AllBuildings)
                    {
                        Rect box = Expand(building.FoundationBounds, .5f);
                        for (float distance = 2f; distance <= 24f; distance += 2f)
                            if (box.Contains(p + direction * distance)) { clear = false; break; }
                        if (!clear) break;
                    }
                    if (clear) { facing = candidate; return true; }
                }
                return false;
            }

            private bool TryHeight(Vector2 p, out float y)
            {
                foreach (var road in roads)
                    if (CityHillsideNeighborhoodPlan.TerrainBounds(road).Contains(p))
                    {
                        float bank = CityHillsideNeighborhoodPlan.SampleBank(layout, road, p);
                        y = CityHillsideNeighborhoodPlan.ApplyFoundationLevels(road, p, bank, housing.Buildings);
                        return true;
                    }
                if (quarter.TerrainBounds.Contains(p) && !quarter.IsExposedRavine(p, 1f))
                { y = quarter.SampleSoil(p); return true; }
                y = 0f; return false;
            }
        }

        private static Rect Expand(Rect r, float amount) => Rect.MinMaxRect(r.xMin - amount, r.yMin - amount, r.xMax + amount, r.yMax + amount);
        private static Vector2[] Rectangle(Vector2 p, Vector3 forward, Vector2 half)
        {
            Vector2 f = new(forward.x, forward.z), r = new(f.y, -f.x);
            return CityRoadPolygon.CounterClockwise(new[] { p - r * half.x - f * half.y,
                p + r * half.x - f * half.y, p + r * half.x + f * half.y, p - r * half.x + f * half.y });
        }
        private static Rect RotatedBounds(Vector2 p, Vector3 forward, Vector2 half, Vector2 offset)
        {
            Vector2 f = new(forward.x, forward.z), right = new(f.y, -f.x);
            p += right * offset.x + f * offset.y;
            Vector2 extent = new(Mathf.Abs(right.x) * half.x + Mathf.Abs(f.x) * half.y,
                Mathf.Abs(right.y) * half.x + Mathf.Abs(f.y) * half.y);
            return Rect.MinMaxRect(p.x - extent.x, p.y - extent.y, p.x + extent.x, p.y + extent.y);
        }
        private static bool Intersects(Rect area, IReadOnlyList<Vector2> polygon)
        {
            if (!area.Overlaps(CityRoadPolygon.Bounds(polygon))) return false;
            IReadOnlyList<Vector2> piece = CityRoadPolygon.Rectangle(area);
            for (int i = 0; i < polygon.Count && piece.Count >= 3; i++)
                piece = CityRoadPolygon.Clip(piece, polygon[i], polygon[(i + 1) % polygon.Count]);
            return Mathf.Abs(CityRoadPolygon.Area(piece)) > .0001f;
        }
    }
}
