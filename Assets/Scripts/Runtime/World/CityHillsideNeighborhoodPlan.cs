using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A fixed-metre building on a cut-and-fill bench, with a continuous public approach.</summary>
    public sealed class CityHillsideBuilding
    {
        internal static readonly Vector2Int GuardedEastHouseCell = new(22, -7);
        internal const float EntryRailWidth = .12f;
        private readonly System.Func<float, float> pavement;
        private readonly float courtEdgeZ;
        public BuildingLot Lot { get; }
        public CityTerraceTransportBank Bank { get; }
        public bool OnSummitStreet { get; }
        public Rect FoundationBounds { get; }
        public IReadOnlyList<Rect> MapFootprints { get; }
        public Rect ApproachBounds { get; }
        public Rect CourtBounds { get; }
        public Vector3 StreetContactWorld { get; }
        public Vector3 DoorWorld { get; }
        public Vector3 ThresholdWorld { get; }
        internal float PavementEdgeZ => StreetContactWorld.z + Mathf.Sign(DoorWorld.z - StreetContactWorld.z);
        internal bool HasGuardedEntry => Bank == CityTerraceTransportBank.East && Lot.Cell == GuardedEastHouseCell;
        internal IReadOnlyList<Vector3> EntryRail { get; } = System.Array.Empty<Vector3>();
        internal IReadOnlyList<Vector2[]> EntryRailFootprints { get; } = System.Array.Empty<Vector2[]>();
        internal const float PavementRise = CityStreetSurfacePlanner.SidewalkTop - CityStreetSurfacePlanner.RoadTop;

        // side = -1 on the south side of this street. A gable entry reaches
        // the footway along the outside wall; its axis never cuts a solid corner.
        internal CityHillsideBuilding(CityTerraceTransportBank bank, Vector2Int cell,
            float x, float streetZ, int side, float setback, int variant, Vector3 forward,
            System.Func<float, float> pavement, bool summit = false, bool playerHome = false)
        {
            Bank = bank;
            OnSummitStreet = summit;
            this.pavement = pavement;
            const CityDistrictKind residential = CityDistrictKind.Residential;
            Vector3 envelope = playerHome ? new Vector3(13f, PlayerHomeBalconyGeometry.PreferredBuildingHeight, 12f)
                : CityBuildingAssetProvider.GetExpectedEnvelope(residential, variant);
            bool gableEntry = Mathf.Abs(forward.x) > .5f;
            var footprint = gableEntry ? new Vector2(envelope.z, envelope.x) : new Vector2(envelope.x, envelope.z);
            float z = streetZ + side * (setback + footprint.y * .5f);
            Quaternion rotation = Quaternion.LookRotation(forward);
            Vector3 entry = playerHome ? forward * (envelope.z * .5f)
                : rotation * CityBuildingAssetProvider.GetExpectedEntranceThreshold(variant);
            float floor = pavement(x);
            ThresholdWorld = new Vector3(x + entry.x, floor, z + entry.z);
            DoorWorld = ThresholdWorld + forward * (gableEntry ? 1.45f : .52f);
            StreetContactWorld = new Vector3(DoorWorld.x, pavement(DoorWorld.x), streetZ + side * 4f);
            var centre = new Vector3(x, floor - CityFacadeGrid.MassBaseElevation, z);
            FoundationBounds = new Rect(new Vector2(x, z) - footprint * .5f, footprint);
            float nearZ = streetZ + side * (setback - 1.3f);
            if (gableEntry)
            {
                float outside = ThresholdWorld.x + forward.x * 3.0f;
                float inside = ThresholdWorld.x + forward.x * .02f;
                CourtBounds = Rect.MinMaxRect(Mathf.Min(inside, outside), z - 2.6f,
                    Mathf.Max(inside, outside), z + 2.6f);
            }
            else
            {
                float atDoor = ThresholdWorld.z + forward.z * .02f;
                float halfCourt = variant == 3 ? 2.35f : 3.25f;
                CourtBounds = Rect.MinMaxRect(DoorWorld.x - halfCourt, Mathf.Min(atDoor, nearZ),
                    DoorWorld.x + halfCourt, Mathf.Max(atDoor, nearZ));
            }
            courtEdgeZ = side < 0 ? CourtBounds.yMax : CourtBounds.yMin;
            ApproachBounds = Rect.MinMaxRect(DoorWorld.x - 1.2f, Mathf.Min(courtEdgeZ, StreetContactWorld.z),
                DoorWorld.x + 1.2f, Mathf.Max(courtEdgeZ, StreetContactWorld.z));
            Lot = new BuildingLot(cell, centre, footprint, envelope.y,
                bank == CityTerraceTransportBank.West ? new Color(.51f, .455f, .40f) : new Color(.46f, .50f, .49f),
                "hillside-residential", residential, CityLandUseKind.Building, false, playerHome, false,
                string.Empty, BarActivityKind.None, playerHome ? Vector2Int.up : Vector2Int.zero, ThresholdWorld,
                playerHome ? new Vector3(StreetContactWorld.x, floor - PavementRise, streetZ) : StreetContactWorld,
                StreetContactWorld, variant, forward);
            MapFootprints = Lot.CreateCollisionFootprints();
            if (HasGuardedEntry)
            {
                const float inset = .09f;
                float approachEdge = ApproachBounds.xMin + inset;
                float porchEdge = CourtBounds.xMin + inset;
                float porchBack = CourtBounds.yMax - inset;
                var rail = new[]
                {
                    RailFoot(approachEdge, PavementEdgeZ + .18f),
                    RailFoot(approachEdge, CourtBounds.yMin),
                    RailFoot(porchEdge, CourtBounds.yMin + inset),
                    RailFoot(porchEdge, porchBack),
                    RailFoot(CourtBounds.xMax - inset, porchBack)
                };
                var footprints = new Vector2[rail.Length - 1][];
                for (int i = 0; i < footprints.Length; i++)
                {
                    Vector2 a = new(rail[i].x, rail[i].z), b = new(rail[i + 1].x, rail[i + 1].z);
                    Vector2 offset = new Vector2(a.y - b.y, b.x - a.x).normalized * (EntryRailWidth * .5f);
                    footprints[i] = CityRoadPolygon.CounterClockwise(new[] { a - offset, b - offset, b + offset, a + offset });
                }
                EntryRail = System.Array.AsReadOnly(rail);
                EntryRailFootprints = System.Array.AsReadOnly(footprints);
            }

            Vector3 RailFoot(float railX, float railZ)
            {
                if (!TrySampleApproach(new Vector2(railX, railZ), out float height))
                    throw new System.InvalidOperationException("The east Ridge entry rail must be on its own pavement.");
                return new Vector3(railX, height - CityHillsideGroundSeamPlan.SurfaceRecess, railZ);
            }
        }

        internal bool HasGlazedBay => !Lot.IsPlayerHome && Bank == CityTerraceTransportBank.West && Lot.BuildingVariant != 4;
        // Two domestic airers share the two spare slots in the city residential
        // wind budget. Their pegs and scheduled fabric sound use the same point.
        internal bool HasAirer => Lot.Cell == new Vector2Int(41, -21) || Lot.Cell == new Vector2Int(21, -5);
        internal Vector3 AirerOrigin
        {
            get
            {
                Vector3 size = CityBuildingAssetProvider.GetExpectedEnvelope(Lot.District, Lot.BuildingVariant);
                Vector3 inRoom = new Vector3(size.x * .28f + .37f, 2.8f, size.z * .5f - .6f);
                return CityBuildingPrototypePlacement.ResolveExpectedCityPose(Lot).TransformPoint(inRoom);
            }
        }
        internal Vector3 PegWorld => AirerOrigin + Lot.FacadeRotation * new Vector3(0f, .94f, .19f);

        public bool TrySampleApproach(Vector2 point, out float top)
        {
            top = DoorWorld.y;
            if (Inside(CourtBounds, point)) return true;
            if (!Inside(ApproachBounds, point)) return false;
            float t = Mathf.InverseLerp(courtEdgeZ, PavementEdgeZ, point.y);
            top = Mathf.Lerp(top, pavement(point.x), t);
            return true;
        }

        internal static bool Inside(Rect r, Vector2 p) => p.x >= r.xMin - .0001f && p.x <= r.xMax + .0001f &&
            p.y >= r.yMin - .0001f && p.y <= r.yMax + .0001f;
    }

    public sealed class CityHillsideNeighborhoodPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityHillsideNeighborhoodPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityHillsideNeighborhoodPlan>();
        public IReadOnlyList<CityHillsideBuilding> Buildings { get; }

        public IReadOnlyList<CityHillsideBuilding> SharedCourtBuildings { get; }
        public IReadOnlyList<CityHillsideBuilding> AllBuildings { get; }

        private CityHillsideNeighborhoodPlan(CityLayout layout)
        {
            var buildings = new List<CityHillsideBuilding>();
            var access = CityHillsideAccessPlan.ForLayout(layout);
            foreach (var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
            {
                bool east = road.Bank == CityTerraceTransportBank.East;
                for (int shelf = 0; shelf < CityHillsideStreetRoute.LaneCount; shelf++)
                {
                    float min = road.LaneMinX, max = road.LaneMaxX;
                    if (shelf == 0)
                    {
                        float mouthX = layout.RoadGeometry.GetNodePosition(road.MouthNode).x;
                        if (east) max = mouthX - 9f;
                        else min = mouthX + 9f;
                    }
                    bool reversed = (shelf & 1) != 0;
                    // Paired west sections step up in height. East wings turn
                    // their gables into the common space between short fronts.
                    for (int parcel = 0; parcel < 3; parcel++)
                    {
                        bool backStreet = shelf < CityHillsideStreetRoute.LaneCount - 1 && parcel == 1;
                        int lane = backStreet ? shelf + 1 : shelf;
                        int side = backStreet ? 1 : -1;
                        int variant = east ? (parcel == 1 ? 4 : parcel == 0 ? 2 : 0)
                            : (parcel == 1 ? 4 : parcel == 0 ? 0 : 1);
                        float fraction = parcel == 0 ? .14f : parcel == 1 ? (east ? .48f : .34f) : .84f;
                        float x = Mathf.Lerp(min, max, reversed ? 1f - fraction : fraction);
                        Vector3 forward = east && parcel == 1 ? (reversed ? Vector3.left : Vector3.right)
                            : Vector3.back * side;
                        var cell = new Vector2Int((east ? 1 : -1) * (21 + parcel), -2 - shelf);
                        float setback = 7.4f + (parcel == 1 ? .65f : 0f);
                        if (east && shelf == CityHillsideStreetRoute.LaneCount - 2 && parcel == 0)
                        {
                            // The rear wing faces the next street uphill. Leave a soil
                            // shoulder between its rear wall and the summit sidewalk.
                            float laneSpacing = Mathf.Abs(road.GetLaneZ(lane) - road.GetLaneZ(lane + 1));
                            float depth = CityBuildingAssetProvider.GetExpectedEnvelope(CityDistrictKind.Residential, variant).z;
                            setback = laneSpacing - CityHillsideStreetRoute.OuterHalfWidth - depth - .65f;
                        }
                        var building = new CityHillsideBuilding(road.Bank, cell, x, road.GetLaneZ(lane), side,
                            setback, variant, forward,
                            at => road.SampleLaneTop(lane, at) + CityHillsideBuilding.PavementRise);
                        if (!BlocksStair(building, access) && HasClearance(building, buildings)) buildings.Add(building);
                    }
                }
                if (road.Bank == CityTerraceTransportBank.West)
                {
                    // Complete the surveyed gap beside the upper footway.
                    // Keep the same cut-and-fill, frontage and shared exterior
                    // contracts as the other west-bank residences.
                    int lane = CityHillsideStreetRoute.LaneCount - 1;
                    var infill = new CityHillsideBuilding(road.Bank, new Vector2Int(-24, -8),
                        -99.2f, road.GetLaneZ(lane), -1, 7.4f, 0, Vector3.forward,
                        at => road.SampleLaneTop(lane, at) + CityHillsideBuilding.PavementRise);
                    if (!BlocksStair(infill, access) && HasClearance(infill, buildings)) buildings.Add(infill);
                    var neighbour = new CityHillsideBuilding(road.Bank, new Vector2Int(-25, -8),
                        -84f, road.GetLaneZ(lane), -1, 7.4f, 4, Vector3.forward,
                        at => road.SampleLaneTop(lane, at) + CityHillsideBuilding.PavementRise);
                    if (!BlocksStair(neighbour, access) && HasClearance(neighbour, buildings)) buildings.Add(neighbour);
                }
            }
            Buildings = buildings.AsReadOnly();
            var shared = new List<CityHillsideBuilding>();
            var links = CityHillsideQuarterPlan.ForLayout(layout).Links;
            if (links.Count != 0)
            {
                var lower = links[0];
                var upper = links[1];
                float lowerZ = lower.WestGateWorld.z;
                float upperZ = upper.WestGateWorld.z;
                // The centreline of the 3.6 m opening belongs to the through walk.
                // The residential entry has its own threshold 3.6 m to the west.
                shared.Add(new CityHillsideBuilding(CityTerraceTransportBank.East, new Vector2Int(40, -21),
                    8f, lowerZ, -1, 6.25f, 3, Vector3.forward, lower.PavementTopAt));
                shared.Add(new CityHillsideBuilding(CityTerraceTransportBank.West, new Vector2Int(-40, -21),
                    -27f, lowerZ, 1, 7.6f, 0, Vector3.back, lower.PavementTopAt));
                shared.Add(new CityHillsideBuilding(CityTerraceTransportBank.East, new Vector2Int(41, -21),
                    29f, lowerZ, 1, 8f, 4, Vector3.left, lower.PavementTopAt));
                shared.Add(layout.SummitHome ?? CreateWestSummitHouse(upper, false));
                shared.Add(new CityHillsideBuilding(CityTerraceTransportBank.East, new Vector2Int(42, -21),
                    29f, upperZ, -1, 8.2f, 2, Vector3.forward, upper.PavementTopAt, true));
            }
            SharedCourtBuildings = shared.AsReadOnly();
            var all = new List<CityHillsideBuilding>(buildings.Count + shared.Count);
            all.AddRange(buildings);
            all.AddRange(shared);
            AllBuildings = all.AsReadOnly();
        }

        internal static CityHillsideBuilding CreateWestSummitHouse(CityHillsideQuarterLink upper, bool playerHome) =>
            new CityHillsideBuilding(CityTerraceTransportBank.West, new Vector2Int(-41, -21),
                playerHome ? -25.5f : -28f, upper.WestGateWorld.z, -1, 8f, 1, Vector3.forward, upper.PavementTopAt, true, playerHome);

        private static bool HasClearance(CityHillsideBuilding house, List<CityHillsideBuilding> placed)
        {
            Rect body = house.FoundationBounds;
            body = Rect.MinMaxRect(body.xMin - .5f, body.yMin - .5f, body.xMax + .5f, body.yMax + .5f);
            foreach (var other in placed)
                if (body.Overlaps(other.FoundationBounds) || body.Overlaps(other.ApproachBounds) ||
                    body.Overlaps(other.CourtBounds) || other.FoundationBounds.Overlaps(house.ApproachBounds) ||
                    other.FoundationBounds.Overlaps(house.CourtBounds)) return false;
            return true;
        }

        private static bool BlocksStair(CityHillsideBuilding b, CityHillsideAccessPlan access)
        {
            foreach (var stair in access.Shortcuts)
                if (b.FoundationBounds.Overlaps(stair.Bounds) || b.CourtBounds.Overlaps(stair.Bounds) ||
                    b.ApproachBounds.Overlaps(stair.Bounds)) return true;
            return false;
        }

        public static CityHillsideNeighborhoodPlan ForLayout(CityLayout layout) => Plans.GetValue(layout, l => new CityHillsideNeighborhoodPlan(l));

        public static Rect TerrainBounds(CityHillsideStreetRoute road) =>
            Rect.MinMaxRect(road.LaneMinX - 26f, road.GetLaneZ(CityHillsideStreetRoute.LaneCount - 1) - 28f,
                road.LaneMaxX + 26f, -168f);

        // The foundation levels shared by the mesh and early shelter placement.
        internal static float ApplyFoundationLevels(CityHillsideStreetRoute road, Vector2 point, float y,
            IReadOnlyList<CityHillsideBuilding> houses)
        {
            for (int index = 0; index < houses.Count; index++)
            {
                var house = houses[index];
                if (house.Bank != road.Bank) continue;
                if (house.TrySampleApproach(point, out float apron))
                {
                    // The inner metre overlaps the existing pavement; recess the
                    // apron there while matching the exact outer edge at five metres.
                    float edgeZ = house.PavementEdgeZ;
                    y = apron - .10f * Mathf.InverseLerp(edgeZ, house.StreetContactWorld.z, point.y);
                    break;
                }
                Rect body = house.FoundationBounds;
                float dx = Mathf.Max(0f, Mathf.Max(body.xMin - point.x, point.x - body.xMax));
                float dz = Mathf.Max(0f, Mathf.Max(body.yMin - point.y, point.y - body.yMax));
                float shoulder = Mathf.Max(dx, dz);
                if (shoulder < 1.3f)
                    y = Mathf.Lerp(house.DoorWorld.y - .10f, y, shoulder / 1.3f);
            }
            return y;
        }

        // Interior of the hillside between two parallel streets. Level shoulders
        // meet sloping cross-banks in the sixteen metres between pavements.
        internal static float SampleBank(CityLayout layout, CityHillsideStreetRoute road, Vector2 p)
        {
            float firstZ = road.GetLaneZ(0);
            float lane = (firstZ - p.y) / (firstZ - road.GetLaneZ(1));
            int lower = Mathf.Clamp(Mathf.FloorToInt(lane), 0, CityHillsideStreetRoute.LaneCount - 1);
            int upper = Mathf.Min(lower + 1, CityHillsideStreetRoute.LaneCount - 1);
            float low = road.SampleLaneTop(lower, p.x);
            float level = low;
            if (p.y > firstZ)
            {
                Vector2 seam = new Vector2(p.x, -167.99f);
                float north = layout.ElevationPlan.TrySampleSurface(seam, CitySurfaceRole.GroundTop, out float y, out _)
                    ? y : road.Profile.Stations[0].Height;
                level = Mathf.Lerp(north, low, Mathf.InverseLerp(-168f, firstZ + 5f, p.y));
            }
            else if (lower != upper)
                level = Mathf.Lerp(low, road.SampleLaneTop(upper, p.x),
                    Mathf.InverseLerp(road.GetLaneZ(lower) - 5f, road.GetLaneZ(upper) + 5f, p.y));
            return level - .20f;
        }
    }
}
