using System;
using System.Collections.Generic;
using UnityEngine;
using static BarPromenade.CityLitterGeometry;

namespace BarPromenade
{
    /// <summary>
    /// Plans the sparse city-wide litter (art §2.3, 2026-09-15). Every
    /// candidate is a seeded slot on a sidewalk edge, in the bare band
    /// around a lot, beside a park bench or path, or on the tide line; a
    /// slot that finds no lawful point or no unrepeated variant is skipped,
    /// never forced. Pure: reads only plans, never the scene.
    /// </summary>
    public static class CityLitterPlanner
    {
        private const uint Salt = 0x2B7E1516u;
        private const int PointAttempts = 8;
        private const float MaximumSlotProbability = .6f;

        // Sidewalk edges: a slot every few metres, most of them empty.
        private const float SidewalkSlotLength = 6f;
        /// <summary>Per slot before lamps, doors, furniture and crossings take their share; about half survive.</summary>
        private const float SidewalkSlotProbability = .34f;
        private const float SidewalkMinimumLength = 4f;
        private const float SidewalkEndInset = .6f;
        private const float SidewalkEdgeInsetMin = .12f;
        private const float SidewalkEdgeInsetMax = .32f;
        private const float SidewalkThickness = CityStreetSurfacePlanner.SidewalkTop - CityStreetSurfacePlanner.RoadTop;
        /// <summary>Wider things would fill a one-metre pavement; they belong on the lot's band.</summary>
        private const float SidewalkMaximumItemSide = .40f;

        // The bare band between a lot's wall and its street.
        private const float LotWallShare = .6f;
        private const float LotWallDistanceMin = .08f;
        private const float LotWallDistanceMax = .6f;
        private const float LotSolidWallDistanceMin = .3f;
        private const float LotSolidWallDistanceMax = 1.2f;
        /// <summary>A solid leaves this much of a walk lane beside it: agent radius plus a step.</summary>
        private const float SolidWalkClearance = .8f;

        // The park: a handful, never a scatter.
        private const int ParkMaximumCount = 6;
        private const float ParkBenchProbability = .45f;
        private const float ParkPathSideProbability = .12f;

        // The beach: the tide line, plus the odd thing at the street edge.
        private const float BeachStrandNear = .7f;
        private const float BeachStrandFar = 3.9f;
        private const float BeachCellLength = 5f;
        private const float BeachCellProbability = .45f;
        private const float BeachStreetEdgeNear = .15f;
        private const float BeachStreetEdgeFar = .9f;
        private const float BeachStreetEdgeProbability = .10f;
        private const float BeachTyreProbability = .06f;

        private const int Glass = 0, Cans = 1, Parts = 2, Plastic = 3, Canister = 4, Bag = 5, Paper = 6, Wood = 7,
            Masonry = 8, Bucket = 9, Crate = 10, Tyre = 11, Bicycle = 12, GroupCount = 13;

        // Weights by group; small and solid groups are drawn separately.
        private static readonly float[] ResidentialTable = { 2f, 2f, 0f, 2f, .5f, 2f, 2f, .5f, .5f, 1f, 1f, .5f, .6f };
        private static readonly float[] NightlifeTable = { 5f, 3f, 0f, 1f, 0f, 1.5f, 2f, 0f, 0f, .5f, .5f, 0f, 0f };
        private static readonly float[] IndustrialTable = { 0f, 1f, 2f, 0f, 2f, 0f, 0f, 3f, 2f, 2f, 2f, 1f, 0f };
        private static readonly float[] OldTownTable = { 2f, 1f, 0f, 0f, 0f, 0f, 1.5f, .5f, .5f, 0f, .5f, 0f, .6f };
        private static readonly float[] BeachTable = { 3f, 2f, 0f, 3f, .5f, 1.5f, 0f, 0f, 0f, 0f, 0f, 1f, 0f };
        private static readonly float[] ParkTable = { 2f, 1.5f, 0f, 0f, 0f, 0f, 1.5f, 0f, 0f, 0f, 0f, 0f, 0f };

        public static CityLitterPlan Create(CityLayout layout, CityNightFixturePlan night, RoadFencePlan fence,
            CityDecorationPlan decoration, CitySeacoastPlan seacoast, CityArchShelterPlan archShelter,
            CityLitterCatalog catalog)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (night == null) throw new ArgumentNullException(nameof(night));
            if (fence == null) throw new ArgumentNullException(nameof(fence));
            if (decoration == null) throw new ArgumentNullException(nameof(decoration));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var session = new Session(layout, night, fence, decoration, seacoast, archShelter, catalog);
            session.PlaceSidewalks();
            session.PlaceLots();
            session.PlacePark();
            session.PlaceBeach();
            return new CityLitterPlan(session.Parts);
        }

        private static float DistrictDensity(CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.Nightlife: return 1.6f;
                case CityDistrictKind.Residential: return 1f;
                case CityDistrictKind.Industrial: return .8f;
                case CityDistrictKind.OldTown: return .6f;
                default: return 0f;
            }
        }

        private static float SolidProbability(CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.Residential: return .12f;
                case CityDistrictKind.Industrial: return .18f;
                case CityDistrictKind.Nightlife: return .06f;
                case CityDistrictKind.OldTown: return .08f;
                default: return 0f;
            }
        }

        private static float[] Table(CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.Residential: return ResidentialTable;
                case CityDistrictKind.Nightlife: return NightlifeTable;
                case CityDistrictKind.Industrial: return IndustrialTable;
                case CityDistrictKind.OldTown: return OldTownTable;
                case CityDistrictKind.NorthWaterfront: return BeachTable;
                case CityDistrictKind.CentralPark: return ParkTable;
                default: return null;
            }
        }

        private static int Group(CityLitterItem item)
        {
            if (item.Solid)
                return item.Category == CityLitterPlan.BicycleCategory ? Bicycle
                    : item.Category == "rubber" ? Tyre : item.Category == "wood" ? Crate : Bucket;
            switch (item.Category)
            {
                case "glass": return Glass;
                case "metal": return item.Name.StartsWith("Can", StringComparison.Ordinal) ? Cans : Parts;
                case "plastic": return item.Name.StartsWith("Canister", StringComparison.Ordinal) ? Canister : Plastic;
                case "bag": return Bag;
                case "paper": return Paper;
                case "wood": return Wood;
                case "masonry": return Masonry;
                default: return -1;
            }
        }

        /// <summary>Items longer than a stride lie along the edge they were dropped beside, not across it.</summary>
        private static bool IsLong(CityLitterItem item) => Mathf.Max(item.Bounds.size.x, item.Bounds.size.z) > .7f;

        private static Vector2 XZ(Vector3 value) => new Vector2(value.x, value.z);
        private static float Next(System.Random random) => (float)random.NextDouble();
        private static Rect LotRect(BuildingLot lot) => Rect.MinMaxRect(lot.Center.x - lot.Size.x * .5f,
            lot.Center.z - lot.Size.y * .5f, lot.Center.x + lot.Size.x * .5f, lot.Center.z + lot.Size.y * .5f);

        private sealed class Session
        {
            private readonly CityLayout layout;
            private readonly CityNightFixturePlan night;
            private readonly RoadFencePlan fence;
            private readonly CityStreetSurfacePlan streets;
            private readonly List<CityLitterItem> items = new List<CityLitterItem>();
            private readonly List<int> groups = new List<int>();
            private readonly Field field = new Field();
            private readonly Occupancy occupancy;
            private readonly Dictionary<Vector2Int, int> surfaceByCell = new Dictionary<Vector2Int, int>();
            private readonly HashSet<Vector2Int> landmarkCells = new HashSet<Vector2Int>();
            private readonly Dictionary<CityDistrictKind, int> bicyclesByDistrict = new Dictionary<CityDistrictKind, int>();
            private readonly List<KeyValuePair<CityLitterItem, float>> candidates = new List<KeyValuePair<CityLitterItem, float>>();
            private int solids, bicycles;

            internal List<CityLitterPart> Parts { get; } = new List<CityLitterPart>();

            internal Session(CityLayout layout, CityNightFixturePlan night, RoadFencePlan fence,
                CityDecorationPlan decoration, CitySeacoastPlan seacoast, CityArchShelterPlan archShelter,
                CityLitterCatalog catalog)
            {
                this.layout = layout;
                this.night = night;
                this.fence = fence;
                streets = CityStreetSurfacePlanner.Create(layout);
                occupancy = new Occupancy(Parts);
                foreach (CityLitterItem item in catalog.Items)
                {
                    int group = Group(item);
                    if (group < 0) continue;
                    items.Add(item);
                    groups.Add(group);
                }
                for (int index = 0; index < layout.Surfaces.Count; index++)
                {
                    CitySurfaceDescriptor surface = layout.Surfaces[index];
                    if (surface.Kind == CitySurfaceKind.RiverWater) continue;
                    if (!surfaceByCell.ContainsKey(surface.Cell)) surfaceByCell.Add(surface.Cell, index);
                }
                foreach (Vector2Int cell in layout.PrimaryLandmarkCells.Values) landmarkCells.Add(cell);
                field.Build(layout, streets, fence, decoration, seacoast, archShelter);
            }

            // ---- zone passes -------------------------------------------------

            internal void PlaceSidewalks()
            {
                for (int index = 0; index < streets.SidewalkGeometry.Count; index++)
                {
                    RuntimeOrientedBox box = streets.SidewalkGeometry[index];
                    // A strip's long axis is whichever local axis is longer: graded
                    // strips look along their slope, flat ones keep the identity.
                    bool zLong = box.Size.z >= box.Size.x;
                    Vector3 along = box.Rotation * (zLong ? Vector3.forward : Vector3.right);
                    along.y = 0f;
                    float length = zLong ? box.Size.z : box.Size.x, width = zLong ? box.Size.x : box.Size.z;
                    Vector3 centre = box.Center;
                    // The port's buried kerb skirt shares this list; it is ten
                    // times a pavement's thickness, so the thickness tells them apart.
                    if (along.sqrMagnitude < .000001f || width > CityStreetSurfacePlanner.SidewalkWidth + .05f ||
                        length < SidewalkMinimumLength || Mathf.Abs(box.Size.y - SidewalkThickness) > .01f) continue;
                    along.Normalize();
                    var lateral = new Vector3(-along.z, 0f, along.x);
                    // The kerb is the side the carriageway lies on; the other side is the lot's band.
                    bool roadPlus = layout.ElevationPlan.TrySampleSurface(XZ(centre + lateral), CitySurfaceRole.RoadTop, out _, out _);
                    bool roadMinus = layout.ElevationPlan.TrySampleSurface(XZ(centre - lateral), CitySurfaceRole.RoadTop, out _, out _);
                    if (roadPlus == roadMinus) continue;
                    Vector3 kerb = roadPlus ? lateral : -lateral;
                    if (!CityTerrainSurfacePlan.TrySampleGroundTop(layout, XZ(centre - kerb), out _, out CitySurfaceDescriptor beside) ||
                        !TryResolveSidewalkDistrict(beside, out CityDistrictKind district, out float density, out bool kerbOnly)) continue;
                    Rect container = Expand(Oriented(centre, box.Rotation, box.Size), -.075f);
                    int slots = Mathf.Max(1, Mathf.FloorToInt(length / SidewalkSlotLength));
                    float slotLength = length / slots;
                    for (int slot = 0; slot < slots; slot++)
                    {
                        string id = "Litter Sidewalk " + index + " " + slot;
                        var random = new System.Random(Seed(id, layout.Seed, Salt));
                        float slotStart = slot * slotLength - length * .5f;
                        Vector2 slotCentre = XZ(centre + along * (slotStart + slotLength * .5f));
                        float probability = Mathf.Min(MaximumSlotProbability,
                            SidewalkSlotProbability * density * field.Attraction(slotCentre));
                        if (Next(random) >= probability) continue;
                        CityLitterItem item = Pick(Table(district), false, slotCentre, district, random, SidewalkMaximumItemSide);
                        if (item == null) continue;
                        for (int attempt = 0; attempt < PointAttempts; attempt++)
                        {
                            float t = Mathf.Clamp(Mathf.Lerp(slotStart + .3f, slotStart + slotLength - .3f, Next(random)),
                                SidewalkEndInset - length * .5f, length * .5f - SidewalkEndInset);
                            bool onKerb = kerbOnly || Next(random) < .5f;
                            float inset = Mathf.Lerp(SidewalkEdgeInsetMin, SidewalkEdgeInsetMax, Next(random));
                            Vector3 point = centre + along * t + (onKerb ? kerb : -kerb) * (width * .5f - inset);
                            if (TryPlace(id, CityLitterZone.Sidewalk, district, beside.Cell, index, container, item,
                                XZ(point), IsLong(item) ? along : Vector3.zero, random)) break;
                        }
                    }
                }
            }

            private bool TryResolveSidewalkDistrict(CitySurfaceDescriptor beside, out CityDistrictKind district,
                out float density, out bool kerbOnly)
            {
                district = default; density = 0f; kerbOnly = false;
                switch (beside.Feature)
                {
                    case CityAreaFeatureKind.UrbanDistrict:
                        if (!layout.TryGetDistrict(beside.AreaId, out CityDistrictDescriptor descriptor)) return false;
                        district = descriptor.Kind;
                        density = DistrictDensity(district);
                        return density > 0f;
                    case CityAreaFeatureKind.CentralPark:
                        // A hedge and a fence stand on the park side; only the kerb collects anything.
                        district = CityDistrictKind.CentralPark; density = .5f; kerbOnly = true;
                        return true;
                    case CityAreaFeatureKind.NorthWaterfront:
                        district = CityDistrictKind.NorthWaterfront; density = .8f;
                        return beside.Kind == CitySurfaceKind.Beach;
                    default:
                        return false;
                }
            }

            internal void PlaceLots()
            {
                var lots = new List<BuildingLot>(layout.BuildingLots);
                lots.Sort((a, b) => a.Cell.y != b.Cell.y ? a.Cell.y.CompareTo(b.Cell.y) : a.Cell.x.CompareTo(b.Cell.x));
                foreach (BuildingLot lot in lots)
                {
                    if (!lot.HasBuilding || lot.IsSupermarket || lot.IsPlayerHome || DistrictDensity(lot.District) <= 0f ||
                        landmarkCells.Contains(lot.Cell) || !surfaceByCell.TryGetValue(lot.Cell, out int surfaceIndex)) continue;
                    CitySurfaceDescriptor surface = layout.Surfaces[surfaceIndex];
                    if (surface.Kind != CitySurfaceKind.BuildableGround) continue;
                    Rect container = surface.WorldBounds, building = LotRect(lot);
                    float[] table = Table(lot.District);
                    string lotId = "Litter Lot " + lot.Cell.x + " " + lot.Cell.y;
                    var lotRandom = new System.Random(Seed(lotId, layout.Seed, Salt));
                    int count = SmallCount(lot.District, lotRandom);
                    for (int slot = 0; slot < count; slot++)
                    {
                        string id = lotId + " " + slot;
                        var random = new System.Random(Seed(id, layout.Seed, Salt));
                        CityLitterItem item = Pick(table, false, XZ(lot.Center), lot.District, random);
                        if (item == null) continue;
                        for (int attempt = 0; attempt < PointAttempts; attempt++)
                        {
                            Vector3 along = Vector3.zero;
                            Vector2 point = Next(random) < LotWallShare
                                ? WallPoint(building, LotWallDistanceMin, LotWallDistanceMax, random, out along)
                                : new Vector2(Mathf.Lerp(container.xMin, container.xMax, Next(random)),
                                    Mathf.Lerp(container.yMin, container.yMax, Next(random)));
                            if (TryPlace(id, CityLitterZone.LotGround, lot.District, lot.Cell, surfaceIndex, container,
                                item, point, IsLong(item) ? along : Vector3.zero, random)) break;
                        }
                    }
                    string solidId = lotId + " Solid";
                    var solidRandom = new System.Random(Seed(solidId, layout.Seed, Salt));
                    if (solids >= CityLitterPlan.MaximumSolidCount || Next(solidRandom) >= SolidProbability(lot.District)) continue;
                    CityLitterItem solid = Pick(table, true, XZ(lot.Center), lot.District, solidRandom);
                    if (solid == null) continue;
                    for (int attempt = 0; attempt < PointAttempts; attempt++)
                    {
                        Vector2 point = WallPoint(building, LotSolidWallDistanceMin, LotSolidWallDistanceMax, solidRandom,
                            out Vector3 along);
                        if (TryPlace(solidId, CityLitterZone.LotGround, lot.District, lot.Cell, surfaceIndex, container,
                            solid, point, IsLong(solid) ? along : Vector3.zero, solidRandom)) break;
                    }
                }
            }

            private static int SmallCount(CityDistrictKind district, System.Random random)
            {
                switch (district)
                {
                    case CityDistrictKind.Residential: return Next(random) < .5f ? 3 : 2;
                    case CityDistrictKind.Nightlife: return 2;
                    case CityDistrictKind.Industrial: return Next(random) < .5f ? 2 : 1;
                    case CityDistrictKind.OldTown: return Next(random) < .6f ? 1 : 0;
                    default: return 0;
                }
            }

            /// <summary>A point a short way out from one of the building's four walls, plus the wall's own direction.</summary>
            private static Vector2 WallPoint(Rect building, float near, float far, System.Random random, out Vector3 along)
            {
                int side = Mathf.Min(3, (int)(Next(random) * 4f));
                float u = Mathf.Lerp(.1f, .9f, Next(random)), d = Mathf.Lerp(near, far, Next(random));
                switch (side)
                {
                    case 0: along = Vector3.forward; return new Vector2(building.xMin - d, Mathf.Lerp(building.yMin, building.yMax, u));
                    case 1: along = Vector3.forward; return new Vector2(building.xMax + d, Mathf.Lerp(building.yMin, building.yMax, u));
                    case 2: along = Vector3.right; return new Vector2(Mathf.Lerp(building.xMin, building.xMax, u), building.yMin - d);
                    default: along = Vector3.right; return new Vector2(Mathf.Lerp(building.xMin, building.xMax, u), building.yMax + d);
                }
            }

            internal void PlacePark()
            {
                CityParkPlan park = layout.Park;
                if (!park.IsEnabled) return;
                int placed = 0;
                var benches = new List<CityParkBenchDescriptor>(park.Benches);
                benches.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                foreach (CityParkBenchDescriptor bench in benches)
                {
                    if (placed >= ParkMaximumCount) return;
                    string id = "Litter Park Bench " + bench.Id;
                    var random = new System.Random(Seed(id, layout.Seed, Salt));
                    if (Next(random) >= ParkBenchProbability) continue;
                    CityLitterItem item = Pick(ParkTable, false, XZ(bench.Position), CityDistrictKind.CentralPark, random);
                    if (item == null) continue;
                    for (int attempt = 0; attempt < PointAttempts; attempt++)
                    {
                        // Beside the seat's end, a little forward: where a hand let it go
                        // without dropping it onto the path the bench faces.
                        Vector3 point = bench.Position + bench.Forward * Mathf.Lerp(-.3f, .6f, Next(random)) +
                            bench.Tangent * ((Next(random) < .5f ? -1f : 1f) * Mathf.Lerp(1.45f, 1.9f, Next(random)));
                        if (!TryFindParkSurface(XZ(point), out int surfaceIndex)) continue;
                        if (TryPlace(id, CityLitterZone.Park, CityDistrictKind.CentralPark, layout.Surfaces[surfaceIndex].Cell,
                            surfaceIndex, layout.Surfaces[surfaceIndex].WorldBounds, item, XZ(point), Vector3.zero, random))
                        { placed++; break; }
                    }
                }
                for (int surfaceIndex = 0; surfaceIndex < layout.Surfaces.Count; surfaceIndex++)
                {
                    CitySurfaceDescriptor surface = layout.Surfaces[surfaceIndex];
                    if (surface.Kind != CitySurfaceKind.ParkGround || surface.Feature != CityAreaFeatureKind.CentralPark) continue;
                    for (int side = 0; side < 4; side++)
                    {
                        if (placed >= ParkMaximumCount) return;
                        if (!IsParkPathSide(surface.Cell, side)) continue;
                        string id = "Litter Park " + surface.Cell.x + " " + surface.Cell.y + " " + side;
                        var random = new System.Random(Seed(id, layout.Seed, Salt));
                        if (Next(random) >= ParkPathSideProbability) continue;
                        Rect bounds = surface.WorldBounds;
                        CityLitterItem item = Pick(ParkTable, false, bounds.center, CityDistrictKind.CentralPark, random);
                        if (item == null) continue;
                        for (int attempt = 0; attempt < PointAttempts; attempt++)
                        {
                            float u = Mathf.Lerp(.1f, .9f, Next(random)), d = Mathf.Lerp(.4f, 1.8f, Next(random));
                            Vector2 point = side == 0 ? new Vector2(bounds.xMin + d, Mathf.Lerp(bounds.yMin, bounds.yMax, u))
                                : side == 1 ? new Vector2(bounds.xMax - d, Mathf.Lerp(bounds.yMin, bounds.yMax, u))
                                : side == 2 ? new Vector2(Mathf.Lerp(bounds.xMin, bounds.xMax, u), bounds.yMin + d)
                                : new Vector2(Mathf.Lerp(bounds.xMin, bounds.xMax, u), bounds.yMax - d);
                            if (TryPlace(id, CityLitterZone.Park, CityDistrictKind.CentralPark, surface.Cell, surfaceIndex,
                                bounds, item, point, Vector3.zero, random))
                            { placed++; break; }
                        }
                    }
                }
            }

            private bool TryFindParkSurface(Vector2 point, out int surfaceIndex)
            {
                surfaceIndex = -1;
                if (!CityTerrainSurfacePlan.TrySampleGroundTop(layout, point, out _, out CitySurfaceDescriptor surface) ||
                    surface.Kind != CitySurfaceKind.ParkGround || !surfaceByCell.TryGetValue(surface.Cell, out surfaceIndex)) return false;
                return layout.Surfaces[surfaceIndex].Equals(surface);
            }

            /// <summary>Sides 0..3 = west, east, south, north; nodes sit on cell corners, so a side is an edge between two.</summary>
            private bool IsParkPathSide(Vector2Int cell, int side)
            {
                Vector2Int a, b;
                switch (side)
                {
                    case 0: a = cell; b = cell + Vector2Int.up; break;
                    case 1: a = cell + Vector2Int.right; b = cell + Vector2Int.one; break;
                    case 2: a = cell; b = cell + Vector2Int.right; break;
                    default: a = cell + Vector2Int.up; b = cell + Vector2Int.one; break;
                }
                var edge = new RoadEdge(a, b);
                return layout.HasRoad(edge) && layout.GetPathKind(edge) == CityPathKind.ParkPath;
            }

            internal void PlaceBeach()
            {
                for (int surfaceIndex = 0; surfaceIndex < layout.Surfaces.Count; surfaceIndex++)
                {
                    CitySurfaceDescriptor surface = layout.Surfaces[surfaceIndex];
                    if (surface.Kind != CitySurfaceKind.Beach || surface.Feature != CityAreaFeatureKind.NorthWaterfront) continue;
                    Rect bounds = surface.WorldBounds;
                    string cellId = "Litter Beach " + surface.Cell.x + " " + surface.Cell.y;
                    Rect strand = Rect.MinMaxRect(bounds.xMin + .5f, bounds.yMax - BeachStrandFar, bounds.xMax - .5f, bounds.yMax - BeachStrandNear);
                    int cells = Mathf.Max(1, Mathf.FloorToInt(strand.width / BeachCellLength));
                    float cellLength = strand.width / cells;
                    for (int index = 0; index < cells; index++)
                    {
                        var slot = Rect.MinMaxRect(strand.xMin + index * cellLength, strand.yMin, strand.xMin + (index + 1) * cellLength, strand.yMax);
                        Scatter(cellId + " " + index, surfaceIndex, bounds, slot, BeachCellProbability, false);
                    }
                    Scatter(cellId + " Edge", surfaceIndex, bounds, Rect.MinMaxRect(bounds.xMin + .5f, bounds.yMin + BeachStreetEdgeNear,
                        bounds.xMax - .5f, bounds.yMin + BeachStreetEdgeFar), BeachStreetEdgeProbability, false);
                    Scatter(cellId + " Tyre", surfaceIndex, bounds, Rect.MinMaxRect(bounds.xMin + .5f, bounds.yMax - BeachStrandFar,
                        bounds.xMax - .5f, bounds.yMax - 2.5f), BeachTyreProbability, true);
                }

                void Scatter(string id, int surfaceIndex, Rect container, Rect slot, float probability, bool solid)
                {
                    var random = new System.Random(Seed(id, layout.Seed, Salt));
                    if (Next(random) >= probability || (solid && solids >= CityLitterPlan.MaximumSolidCount)) return;
                    CityLitterItem item = Pick(BeachTable, solid, slot.center, CityDistrictKind.NorthWaterfront, random);
                    if (item == null) return;
                    Vector2Int cell = layout.Surfaces[surfaceIndex].Cell;
                    for (int attempt = 0; attempt < PointAttempts; attempt++)
                    {
                        var point = new Vector2(Mathf.Lerp(slot.xMin, slot.xMax, Next(random)), Mathf.Lerp(slot.yMin, slot.yMax, Next(random)));
                        if (TryPlace(id, CityLitterZone.Beach, CityDistrictKind.NorthWaterfront, cell, surfaceIndex, container, item,
                            point, IsLong(item) ? Vector3.right : Vector3.zero, random)) break;
                    }
                }
            }

            // ---- shared acceptance ---------------------------------------------

            /// <summary>
            /// Weighted draw over the zone's table, minus every variant already
            /// lying within its own radius and, when a choice remains, minus the
            /// nearest neighbour's whole category. Null means the slot stays empty.
            /// </summary>
            private CityLitterItem Pick(float[] table, bool solid, Vector2 point, CityDistrictKind district, System.Random random,
                float maximumSide = float.PositiveInfinity)
            {
                if (table == null) return null;
                candidates.Clear();
                float total = 0f;
                for (int index = 0; index < items.Count; index++)
                {
                    CityLitterItem item = items[index];
                    float weight = table[groups[index]];
                    if (item.Solid != solid || weight <= 0f ||
                        Mathf.Min(item.Bounds.size.x, item.Bounds.size.z) > maximumSide) continue;
                    if (item.Category == CityLitterPlan.BicycleCategory &&
                        (bicycles >= CityLitterPlan.MaximumBicycleCount ||
                         (bicyclesByDistrict.TryGetValue(district, out int inDistrict) &&
                          inDistrict >= CityLitterPlan.MaximumBicyclesPerDistrict))) continue;
                    if (occupancy.HasVariantWithin(item.Name, point, CityLitterPlan.SameVariantRadius(item))) continue;
                    candidates.Add(new KeyValuePair<CityLitterItem, float>(item, weight));
                    total += weight;
                }
                if (candidates.Count == 0) return null;
                if (occupancy.TryNearest(point, CityLitterPlan.NeighbourCategoryRadius, out CityLitterPart nearest))
                {
                    string category = nearest.Item.Category;
                    float differing = 0f;
                    foreach (KeyValuePair<CityLitterItem, float> candidate in candidates)
                        if (candidate.Key.Category != category) differing += candidate.Value;
                    if (differing > 0f)
                    {
                        candidates.RemoveAll(candidate => candidate.Key.Category == category);
                        total = differing;
                    }
                }
                double roll = random.NextDouble() * total;
                foreach (KeyValuePair<CityLitterItem, float> candidate in candidates)
                {
                    roll -= candidate.Value;
                    if (roll < 0d) return candidate.Key;
                }
                return candidates[candidates.Count - 1].Key;
            }

            private bool TryPlace(string id, CityLitterZone zone, CityDistrictKind district, Vector2Int cell, int surfaceIndex,
                Rect container, CityLitterItem item, Vector2 point, Vector3 along, System.Random random)
            {
                if (Parts.Count >= CityLitterPlan.MaximumPartCount) return false;
                float scale = .92f + Next(random) * .16f;
                // A long item lies along the edge it was dropped beside; its
                // own long axis may be authored along X or Z.
                float yawDegrees = along == Vector3.zero
                    ? Next(random) * 360f
                    : Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg + (Next(random) - .5f) * 24f -
                      (item.Bounds.size.x > item.Bounds.size.z ? 90f : 0f);
                Quaternion yaw = Quaternion.Euler(0f, yawDegrees, 0f);
                var flat = new Vector3(point.x, 0f, point.y);
                if (!IsAllowed(Project(item.Bounds, flat, yaw, scale), zone, item.Solid, container)) return false;
                float height;
                Vector3 normal;
                if (zone == CityLitterZone.Sidewalk)
                {
                    RuntimeOrientedBox box = streets.SidewalkGeometry[surfaceIndex];
                    if (!box.TrySampleTop(flat, out height)) return false;
                    normal = box.Rotation * Vector3.up;
                    // Road caps and corner pads overlap graded strips by a few
                    // centimetres; a bottle sunk into asphalt reads as a bug.
                    if (field.IsOverlaid(Project(item.Bounds, flat, yaw, scale), box, surfaceIndex)) return false;
                }
                else
                {
                    CitySurfaceDescriptor surface = layout.Surfaces[surfaceIndex];
                    height = CityTerrainSurfacePlan.SampleTop(layout, surface, point);
                    normal = CityTerrainSurfacePlan.SampleNormal(layout, surface, point);
                }
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal.normalized) * yaw;
                var position = new Vector3(point.x, height, point.y);
                Rect footprint = Project(item.Bounds, position, rotation, scale);
                if (!IsAllowed(footprint, zone, item.Solid, container)) return false;
                if (zone == CityLitterZone.Beach && !RestsOnFirmSand(layout.Surfaces[surfaceIndex], footprint)) return false;
                if (occupancy.OverlapsClearance(footprint, CityLitterPlan.MinimumItemClearance)) return false;
                if (occupancy.HasVariantWithin(item.Name, point, CityLitterPlan.SameVariantRadius(item))) return false;
                float radius = Mathf.Sqrt(footprint.width * footprint.width + footprint.height * footprint.height) * .5f;
                if (CityDecorationValidator.IsProtectedGroundAnchor(position, radius + (item.Solid ? SolidWalkClearance : 0f), fence, night))
                    return false;
                if (layout.IsWater(position) || layout.IsWater(new Vector3(footprint.xMin, height, footprint.yMin)) ||
                    layout.IsWater(new Vector3(footprint.xMax, height, footprint.yMin)) ||
                    layout.IsWater(new Vector3(footprint.xMin, height, footprint.yMax)) ||
                    layout.IsWater(new Vector3(footprint.xMax, height, footprint.yMax))) return false;
                Parts.Add(new CityLitterPart(id, item, zone, district, cell, surfaceIndex, position, rotation, scale, footprint));
                occupancy.Add(Parts.Count - 1);
                if (item.Solid) solids++;
                if (item.Category == CityLitterPlan.BicycleCategory)
                {
                    bicycles++;
                    bicyclesByDistrict.TryGetValue(district, out int inDistrict);
                    bicyclesByDistrict[district] = inDistrict + 1;
                }
                return true;
            }

            private bool IsAllowed(Rect footprint, CityLitterZone zone, bool solid, Rect container)
            {
                if (!Contains(container, footprint)) return false;
                if (field.Blocks(footprint, zone != CityLitterZone.Sidewalk)) return false;
                return !solid || !field.BlocksWalkLane(Expand(footprint, SolidWalkClearance));
            }

            /// <summary>The drawn sand carries loose grains the collider does not; litter lies only where that depth is exactly zero.</summary>
            private bool RestsOnFirmSand(CitySurfaceDescriptor surface, Rect footprint)
            {
                return CityBeachSandPlan.SampleLooseDepth(layout.ElevationPlan, surface, footprint.center) == 0f &&
                       CityBeachSandPlan.SampleLooseDepth(layout.ElevationPlan, surface, footprint.min) == 0f &&
                       CityBeachSandPlan.SampleLooseDepth(layout.ElevationPlan, surface, footprint.max) == 0f &&
                       CityBeachSandPlan.SampleLooseDepth(layout.ElevationPlan, surface, new Vector2(footprint.xMin, footprint.yMax)) == 0f &&
                       CityBeachSandPlan.SampleLooseDepth(layout.ElevationPlan, surface, new Vector2(footprint.xMax, footprint.yMin)) == 0f;
            }
        }

        /// <summary>Everything the litter must keep off, gathered once from the other plans.</summary>
        private sealed class Field
        {
            private readonly struct Circle
            {
                internal Circle(Vector2 center, float radius) { Center = center; Radius = radius; }
                internal readonly Vector2 Center;
                internal readonly float Radius;
            }

            private readonly struct Ring
            {
                internal Ring(Vector2 center, float inner, float outer, float multiplier)
                { Center = center; Inner = inner; Outer = outer; Multiplier = multiplier; }
                internal readonly Vector2 Center;
                internal readonly float Inner, Outer, Multiplier;
            }

            private readonly List<Rect> blocked = new List<Rect>();
            private readonly List<Rect> roads = new List<Rect>();
            private readonly List<Rect> walkLanes = new List<Rect>();
            private readonly List<Circle> circles = new List<Circle>();
            private readonly List<Ring> attractors = new List<Ring>();
            private readonly List<KeyValuePair<Rect, RuntimeOrientedBox>> streetBoxes = new List<KeyValuePair<Rect, RuntimeOrientedBox>>();
            private readonly List<KeyValuePair<Rect, RuntimeOrientedBox>> sidewalkBoxes = new List<KeyValuePair<Rect, RuntimeOrientedBox>>();

            internal void Build(CityLayout layout, CityStreetSurfacePlan streets, RoadFencePlan fence,
                CityDecorationPlan decoration, CitySeacoastPlan seacoast, CityArchShelterPlan archShelter)
            {
                foreach (Rect road in layout.CreateRoadRects()) roads.Add(road);
                // The built boxes reach past the nominal road band: dead-end caps,
                // corner pads and stair approaches. Ground litter keeps off them all.
                foreach (RuntimeOrientedBox box in streets.StreetGeometry)
                {
                    Rect rect = Oriented(box.Center, box.Rotation, box.Size);
                    streetBoxes.Add(new KeyValuePair<Rect, RuntimeOrientedBox>(rect, box));
                    roads.Add(Expand(rect, .05f));
                }
                foreach (RuntimeOrientedBox box in streets.SidewalkGeometry)
                {
                    Rect rect = Oriented(box.Center, box.Rotation, box.Size);
                    sidewalkBoxes.Add(new KeyValuePair<Rect, RuntimeOrientedBox>(rect, box));
                    roads.Add(Expand(rect, .05f));
                }
                foreach (Rect crossing in streets.CrosswalkWalkableRectangles) { blocked.Add(Expand(crossing, .25f)); walkLanes.Add(crossing); }
                foreach (Rect sidewalk in streets.SidewalkWalkableRectangles) walkLanes.Add(sidewalk);
                // Route 01's shelters and poles stand where the bus plan puts them,
                // a lane's length from the decoration that seeded them.
                foreach (CityBusStopDescriptor stop in CityBusPlanner.Create(layout, decoration).Stops)
                {
                    circles.Add(new Circle(XZ(stop.ShelterPosition), 5.5f));
                    circles.Add(new Circle(XZ(stop.Position), 1.5f));
                }
                foreach (BuildingLot lot in layout.BuildingLots)
                {
                    if (!lot.HasBuilding) continue;
                    // A bottle may lie against a wall, never inside one; doors and the way to them stay clear.
                    blocked.Add(Expand(LotRect(lot), .04f));
                    circles.Add(new Circle(XZ(lot.DoorPosition), lot.IsBar ? 2.5f : 1.8f));
                    circles.Add(new Circle(XZ(lot.SidewalkArrivalPosition), 1f));
                }
                foreach (CityDistrictPointOfInterestDescriptor point in layout.DistrictPointsOfInterest)
                {
                    blocked.Add(Expand(point.PublicBounds, 1f));
                    foreach (CityDistrictPointOfInterestAccessDescriptor access in point.Accesses)
                    { blocked.Add(Expand(access.ApproachBounds, .5f)); walkLanes.Add(access.ApproachBounds); }
                }
                foreach (CityOpenAreaAccessDescriptor access in layout.OpenAreaAccesses)
                { blocked.Add(Expand(access.ApproachBounds, .5f)); walkLanes.Add(access.ApproachBounds); }
                foreach (CitySurfaceDescriptor surface in layout.Surfaces)
                {
                    // Cared-for ground and every yard (the east keeps its own denser plan).
                    if (surface.Kind == CitySurfaceKind.CemeteryGround || surface.Kind == CitySurfaceKind.ChurchGround ||
                        surface.IsWater || surface.Feature == CityAreaFeatureKind.Yard ||
                        surface.Feature == CityAreaFeatureKind.Cemetery || surface.Feature == CityAreaFeatureKind.Church)
                        blocked.Add(Expand(surface.WorldBounds, .5f));
                }
                if (archShelter != null && archShelter.IsEnabled)
                {
                    CityArchShelterPlacement placement = archShelter.Placement;
                    blocked.Add(Expand(placement.CommonFacadeFootprint, 1.5f));
                    blocked.Add(Expand(placement.PassageFootprint, 1.5f));
                    blocked.Add(Expand(placement.ShelteredFootprint, 1.5f));
                    blocked.Add(Expand(placement.TableauFootprint, 1.5f));
                    blocked.Add(Expand(placement.RailSuppressionFootprint, 1.5f));
                    blocked.Add(Expand(archShelter.Steps.Footprint, 1f));
                    blocked.Add(Expand(archShelter.UpperLanding.Footprint, 1f));
                    blocked.Add(Expand(archShelter.Platform.Footprint, 1f));
                    foreach (CityArchShelterPropDescriptor prop in archShelter.Props) blocked.Add(Expand(ToXZ(prop.Bounds), 1f));
                    foreach (CityArchShelterObstacleDescriptor obstacle in archShelter.Obstacles) blocked.Add(Expand(ToXZ(obstacle.Bounds), .5f));
                    foreach (CityArchShelterClearLaneDescriptor lane in archShelter.ClearLanes) blocked.Add(Expand(lane.Footprint, .5f));
                    foreach (CityArchShelterNpcAnchorDescriptor anchor in archShelter.NpcAnchors) circles.Add(new Circle(XZ(anchor.Position), 1f));
                }
                HomeYardSitePlan? homeYard = HomeYardSitePlanner.Create(layout);
                if (homeYard.HasValue) blocked.Add(Expand(homeYard.Value.GroundBounds, HomeYardSitePlanner.WallMargin + 1f));
                if (seacoast != null)
                {
                    if (seacoast.Port != null)
                    {
                        blocked.Add(Expand(seacoast.Port.LandBounds, 3f));
                        if (seacoast.Port.Access != null)
                        {
                            blocked.Add(Expand(seacoast.Port.Access.GradedBounds, 1f));
                            blocked.Add(Expand(seacoast.Port.Access.ReservedBounds, 1f));
                        }
                    }
                    foreach (CitySeacoastPartDescriptor part in seacoast.Parts)
                        blocked.Add(Expand(Oriented(part.Center, part.Rotation, part.Size), .6f));
                    foreach (CitySeacoastLampDescriptor lamp in seacoast.Lamps) circles.Add(new Circle(XZ(lamp.GroundPosition), 1f));
                    Rect row = seacoast.Frame.BeachRowBounds;
                    blocked.Add(Rect.MinMaxRect(seacoast.Frame.ChannelXMin - 1.5f, row.yMin, seacoast.Frame.ChannelXMax + 1.5f, row.yMax));
                }
                if (layout.River.IsEnabled)
                {
                    foreach (CityRiverPromenadeDescriptor promenade in layout.River.Promenades) blocked.Add(Expand(promenade.Bounds, .5f));
                    foreach (CityRiverLandingDescriptor landing in layout.River.Landings)
                    { blocked.Add(Expand(landing.StairBounds, .5f)); blocked.Add(Expand(landing.PlatformBounds, .5f)); }
                    foreach (CityRiverBridgeDescriptor bridge in layout.River.Bridges) blocked.Add(Expand(bridge.DeckBounds, .5f));
                }
                foreach (CityElevationStairDescriptor stair in layout.ElevationPlan.SignatureStairs)
                {
                    CityElevationStairPlacement placement = CityElevationStairPlacementPlanner.Create(layout, stair);
                    blocked.Add(Expand(placement.Footprint, .5f));
                    blocked.Add(Expand(placement.LowerApproachFootprint, .5f));
                    blocked.Add(Expand(placement.UpperApproachFootprint, .5f));
                    blocked.Add(Expand(placement.GroundCutFootprint, .5f));
                    walkLanes.Add(placement.LowerApproachFootprint);
                    walkLanes.Add(placement.UpperApproachFootprint);
                }
                foreach (RoadFenceSegmentDescriptor segment in fence.Segments)
                    blocked.Add(Expand(Rect.MinMaxRect(Mathf.Min(segment.Start.x, segment.End.x), Mathf.Min(segment.Start.z, segment.End.z),
                        Mathf.Max(segment.Start.x, segment.End.x), Mathf.Max(segment.Start.z, segment.End.z)), .35f));
                if (layout.Park.IsEnabled)
                {
                    foreach (Vector3 tree in layout.Park.TreePositions) circles.Add(new Circle(XZ(tree), .75f));
                    foreach (CityParkGateDescriptor gate in layout.Park.Gates) circles.Add(new Circle(XZ(gate.Center), gate.Width * .5f + 1.25f));
                    foreach (CityParkRegionPlan region in layout.Park.Regions) circles.Add(new Circle(XZ(region.PlazaPosition), 6f));
                    foreach (CityParkBenchDescriptor bench in layout.Park.Benches)
                        blocked.Add(Expand(Oriented(bench.Position, bench.Rotation,
                            new Vector3(CityParkBenchDescriptor.SeatWidth, 1f, CityParkBenchDescriptor.SeatDepth)), .15f));
                }
                foreach (CityDecorationDescriptor descriptor in decoration.Descriptors)
                {
                    switch (descriptor.AnchorKind)
                    {
                        case CityDecorationAnchorKind.BuildingFrontage:
                        case CityDecorationAnchorKind.LotGround:
                        case CityDecorationAnchorKind.Roadside:
                        case CityDecorationAnchorKind.ParkFeature:
                        case CityDecorationAnchorKind.ParkLandmark:
                            circles.Add(new Circle(XZ(descriptor.Position), CityDecorationValidator.ResolveProtectionRadius(descriptor.Kind)));
                            break;
                        case CityDecorationAnchorKind.UrbanLandmark:
                            circles.Add(new Circle(XZ(descriptor.Position), 3f));
                            break;
                        default:
                            continue;
                    }
                    // Where people wait, eat or throw things away, more ends up on the ground.
                    switch (descriptor.Kind)
                    {
                        case CityDecorationKind.RoadsideDumpsterAndUtility: attractors.Add(new Ring(XZ(descriptor.Position), 2.7f, 6.5f, 2.5f)); break;
                        case CityDecorationKind.NightlifeVendingAndQueue: attractors.Add(new Ring(XZ(descriptor.Position), 3.3f, 7f, 2.5f)); break;
                        case CityDecorationKind.RoadsideBusShelter: attractors.Add(new Ring(XZ(descriptor.Position), 4.6f, 8f, 2f)); break;
                        case CityDecorationKind.ResidentialDiscardedFurniture: attractors.Add(new Ring(XZ(descriptor.Position), 3f, 6f, 1.8f)); break;
                        case CityDecorationKind.OldTownStreetMarket: attractors.Add(new Ring(XZ(descriptor.Position), 3.3f, 6.5f, 1.5f)); break;
                    }
                }
            }

            internal bool Blocks(Rect footprint, bool ground)
            {
                foreach (Rect rect in blocked) if (rect.Overlaps(footprint)) return true;
                if (ground) foreach (Rect rect in roads) if (rect.Overlaps(footprint)) return true;
                foreach (Circle circle in circles)
                {
                    float dx = circle.Center.x - Mathf.Clamp(circle.Center.x, footprint.xMin, footprint.xMax);
                    float dz = circle.Center.y - Mathf.Clamp(circle.Center.y, footprint.yMin, footprint.yMax);
                    if (dx * dx + dz * dz < circle.Radius * circle.Radius) return true;
                }
                return false;
            }

            /// <summary>True when another built street or pavement box rises to or above this strip under the footprint.</summary>
            internal bool IsOverlaid(Rect footprint, RuntimeOrientedBox own, int ownIndex)
            {
                for (int list = 0; list < 2; list++)
                {
                    List<KeyValuePair<Rect, RuntimeOrientedBox>> boxes = list == 0 ? streetBoxes : sidewalkBoxes;
                    for (int index = 0; index < boxes.Count; index++)
                    {
                        if ((list == 1 && index == ownIndex) || !boxes[index].Key.Overlaps(footprint)) continue;
                        RuntimeOrientedBox other = boxes[index].Value;
                        for (int corner = 0; corner < 5; corner++)
                        {
                            var point = new Vector3(corner == 4 ? footprint.center.x : (corner & 1) == 0 ? footprint.xMin : footprint.xMax, 0f,
                                corner == 4 ? footprint.center.y : (corner & 2) == 0 ? footprint.yMin : footprint.yMax);
                            if (other.TrySampleTop(point, out float top) && own.TrySampleTop(point, out float mine) && top > mine - .01f)
                                return true;
                        }
                    }
                }
                return false;
            }

            internal bool BlocksWalkLane(Rect expanded)
            {
                foreach (Rect rect in walkLanes) if (rect.Overlaps(expanded)) return true;
                return false;
            }

            internal float Attraction(Vector2 point)
            {
                float factor = 1f;
                foreach (Ring ring in attractors)
                {
                    float distance = Vector2.Distance(ring.Center, point);
                    if (distance >= ring.Inner && distance <= ring.Outer) factor = Mathf.Max(factor, ring.Multiplier);
                }
                return factor;
            }
        }

        /// <summary>Placed parts bucketed on an 18 m grid so clearance and repeat checks stay local.</summary>
        private sealed class Occupancy
        {
            private const float Cell = 18f;
            private readonly List<CityLitterPart> parts;
            private readonly Dictionary<long, List<int>> buckets = new Dictionary<long, List<int>>();

            internal Occupancy(List<CityLitterPart> parts) { this.parts = parts; }

            internal void Add(int index)
            {
                long key = Key(XZ(parts[index].Position));
                if (!buckets.TryGetValue(key, out List<int> bucket)) buckets.Add(key, bucket = new List<int>());
                bucket.Add(index);
            }

            internal bool OverlapsClearance(Rect footprint, float clearance)
            {
                Rect expanded = Expand(footprint, clearance);
                foreach (int index in Near(footprint.center, clearance + Mathf.Max(footprint.width, footprint.height)))
                    if (parts[index].Footprint.Overlaps(expanded)) return true;
                return false;
            }

            internal bool HasVariantWithin(string name, Vector2 point, float radius)
            {
                foreach (int index in Near(point, radius))
                {
                    CityLitterPart part = parts[index];
                    if (part.Item.Name == name && Vector2.Distance(XZ(part.Position), point) <= radius) return true;
                }
                return false;
            }

            internal bool TryNearest(Vector2 point, float radius, out CityLitterPart nearest)
            {
                nearest = default;
                float best = radius;
                bool found = false;
                foreach (int index in Near(point, radius))
                {
                    float distance = Vector2.Distance(XZ(parts[index].Position), point);
                    if (distance > best) continue;
                    best = distance; nearest = parts[index]; found = true;
                }
                return found;
            }

            private IEnumerable<int> Near(Vector2 point, float radius)
            {
                int reach = Mathf.CeilToInt(radius / Cell);
                int cx = Mathf.FloorToInt(point.x / Cell), cz = Mathf.FloorToInt(point.y / Cell);
                for (int x = cx - reach; x <= cx + reach; x++)
                for (int z = cz - reach; z <= cz + reach; z++)
                    if (buckets.TryGetValue(Key(x, z), out List<int> bucket))
                        foreach (int index in bucket) yield return index;
            }

            private static long Key(Vector2 point) => Key(Mathf.FloorToInt(point.x / Cell), Mathf.FloorToInt(point.y / Cell));
            private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
        }
    }
}
