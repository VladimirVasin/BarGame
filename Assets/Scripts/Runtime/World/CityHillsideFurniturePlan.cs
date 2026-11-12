using System;
using System.Collections.Generic;
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
            foreach (var item in raw.Sites)
            {
                bool onCourt = item.Id == courtPhone;
                var candidate = onCourt ? new CityHillsideFurniturePlacement(item.Id, item.Kind, court.Phone,
                    Vector3.right, court.Phone + Vector3.right * 3f, court.PhoneBounds, court.PhoneAccess, item.Variant) : item;
                Vector2 point = new(candidate.Position.x, candidate.Position.z);
                float top = candidate.Position.y;
                if (!onCourt && !ground.TrySampleTop(point, out top))
                { if (candidate.Id == HomeBenchId) homeFailure = "no ground at " + point; continue; }
                bool clear = true;
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
                Vector2 half = placed.Kind == CityMiscKind.ParkBench ? new Vector2(1.15f, .36f)
                    : placed.Kind == CityMiscKind.RoadsidePhoneBooth ? new Vector2(.74f, .57f) : new Vector2(1.98f, .60f);
                polygons.Add(Rectangle(point, placed.Forward, half));
                if (placed.Kind == CityMiscKind.ParkBench)
                    seats.Add(new CityBenchSeat(placed.Id, placed.Position + Vector3.up * CityBenchSitPlan.ParkSeatTopHeight,
                        CityBenchSitPlan.ParkSeatWidth, CityBenchSitPlan.ParkSeatDepth, top, placed.Forward));
                else
                {
                    bool phone = placed.Kind == CityMiscKind.RoadsidePhoneBooth;
                    Vector3 side = Vector3.Cross(Vector3.up, placed.Forward);
                    Vector3 dock = placed.Position + placed.Forward * (phone ? 1.137f : 1.106f);
                    // Legacy utility recipes reflect local X before import; the lid is on
                    // the sitter's right, matching CityDecorationWorldBuilder's tangent.
                    if (!phone) dock += side * .65f;
                    docks.Add(new CityStreetUtilityDock(placed.Id + (phone ? "-door" : "-lid"), placed.Id,
                        phone ? CityStreetUtilityKind.PhoneBooth : CityStreetUtilityKind.Dumpster, dock, -placed.Forward));
                }
            }
            if (layout.HasSummitHome && !seats.Exists(s => s.Id == HomeBenchId))
                throw new InvalidOperationException("The Ridge home viewpoint needs a supported, unobstructed bench: " + homeFailure);
            if (layout.HasSummitHome && !placements.Exists(p => p.Id == LoftPhoneId))
                throw new InvalidOperationException("The pigeon courtyard phone needs its supported public landing.");
            Placements = placements.AsReadOnly(); Seats = seats.AsReadOnly();
            UtilityDocks = docks.AsReadOnly(); SolidFootprints = solids.AsReadOnly(); SolidPolygons = polygons.AsReadOnly();
        }

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
