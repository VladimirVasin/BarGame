using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The first occupied terrace precinct. All geometry is local to Origin;
    /// reservations, cuts, entrance contacts and height queries use world metres.</summary>
    public sealed partial class CityTerraceDistrictPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityTerraceDistrictPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityTerraceDistrictPlan>();
        private static readonly Vector2Int[] Cells = {
            new Vector2Int(9, 7), new Vector2Int(10, 7),
            new Vector2Int(9, 8), new Vector2Int(10, 8), new Vector2Int(11, 8) };
        private static readonly RoadEdge EntryEdge = new RoadEdge(new Vector2Int(9, 8), new Vector2Int(10, 8));
        private const float OriginX = 99.9f;
        private const float OriginZ = 52f;
        private const float HouseCenterX = 89.19f;
        private const float MiddleRise = CityTerracePilotPlan.FlightRise * 2f;
        private readonly List<Rect> cuts = new List<Rect>();
        private readonly List<Rect> throats = new List<Rect>();
        private readonly List<Vector3> houseEntries = new List<Vector3>();

        private CityTerraceDistrictPlan(CityLayout layout)
        {
            Pilot = CityTerracePilotPlan.Create();
            ReservedCells = Array.AsReadOnly(Cells);
            var floors = new List<CityTerracePilotFloor>(Pilot.Floors);
            Floors = floors.AsReadOnly();
            var flights = new List<CityExteriorStairFlightDescriptor>(Pilot.Flights);
            Flights = flights.AsReadOnly();
            GroundCuts = cuts.AsReadOnly();
            HouseThroats = throats.AsReadOnly();
            HouseEntryWorld = houseEntries.AsReadOnly();
            if (layout.BlueprintId != CityBlueprintCatalog.DefaultBlueprintId ||
                !layout.SpatialPlan.IsCompactCoastal || !HasReservation(layout.BuildingLots)) return;

            IsEnabled = true;
            Origin = new Vector3(OriginX,
                layout.ElevationPlan.GetNodeElevation(new Vector2Int(10, 8)) + CityStreetSurfacePlanner.RoadTop,
                OriginZ);
            ReservedBounds = Rect.MinMaxRect(83f, 31f, 125f, 73f);
            EntryEndWorld = World(new Vector3(0f, 0f, -5f));
            CityRoadProjection projection = layout.RoadGeometry.Get(EntryEdge).Project(new Vector2(84f, OriginZ));
            EntryStartWorld = new Vector3(84f, layout.ElevationPlan.SampleRoadDatum(EntryEdge,
                projection.DistanceAlong / layout.GetRoadLength(EntryEdge)) + CityStreetSurfacePlanner.RoadTop, OriginZ);
            EntryBounds = Rect.MinMaxRect(EntryStartWorld.x, 47f, EntryEndWorld.x, 57f);
            EntryGradePercent = Mathf.Abs(EntryEndWorld.y - EntryStartWorld.y) /
                (EntryEndWorld.x - EntryStartWorld.x) * 100f;
            if (EntryGradePercent > 8.3f + .001f)
                throw new InvalidOperationException("Terrace street approach exceeds its 8.3% pedestrian grade.");

            LowerPassStartWorld = World(new Vector3(0f, 0f, 5f));
            var eastSocket = new Vector2(126.4f, OriginZ);
            if (!layout.ElevationPlan.TrySampleSurface(eastSocket, CitySurfaceRole.RoadTop,
                out float eastTop, out _))
                throw new InvalidOperationException("The terrace lower passage needs its existing eastern street socket.");
            LowerPassEndWorld = new Vector3(eastSocket.x, eastTop, eastSocket.y);
            LowerPassRampEndWorld = LowerPassEndWorld - Vector3.right * EastLandingLength;
            var eastLanding = new CityTerracePilotFloor(EastLandingFloorId,
                Local((LowerPassRampEndWorld + LowerPassEndWorld) * .5f),
                new Vector2(LowerPassWidth, EastLandingLength), false, "TerraceCourt", .30f);
            floors.Add(eastLanding);
            EastLandingBounds = WorldBounds(eastLanding.Bounds);
            // Keep the whole corridor reserved; only its last 2.4 metres are
            // level. The public socket remains in the existing street mouth.
            LowerPassBounds = Rect.MinMaxRect(LowerPassStartWorld.x, OriginZ - LowerPassWidth * .5f,
                LowerPassEndWorld.x, OriginZ + LowerPassWidth * .5f);
            LowerPassRampBounds = Rect.MinMaxRect(LowerPassStartWorld.x, LowerPassBounds.yMin,
                LowerPassRampEndWorld.x, LowerPassBounds.yMax);
            LowerPassGradePercent = Mathf.Abs(LowerPassRampEndWorld.y - LowerPassStartWorld.y) /
                (LowerPassRampEndWorld.x - LowerPassStartWorld.x) * 100f;
            if (LowerPassGradePercent > 8.3f + .001f)
                throw new InvalidOperationException("The terrace lower passage exceeds its 8.3% pedestrian grade.");
            for (int i = 0; i < floors.Count; i++)
            {
                CityTerracePilotFloor floor = floors[i];
                if (floor.Id != "west-upper-court" && floor.Id != "east-upper-court") continue;
                floors[i] = new CityTerracePilotFloor(floor.Id,
                    new Vector3(Mathf.Sign(floor.Center.x) * 6.2f, Pilot.TotalRise, 20.08f),
                    new Vector2(7.6f, 10f), true, "TerraceUpperCourt");
            }
            var bridge = new CityTerracePilotFloor(BridgeFloorId,
                new Vector3(0f, Pilot.TotalRise, 20.08f), new Vector2(4.8f, 2.4f), true,
                "TerraceBridge", .35f, false);
            floors.Add(bridge);
            BridgeWorldBounds = WorldBounds(bridge.Bounds);

            foreach (int side in new[] { -1, 1 })
            {
                floors.Add(new CityTerracePilotFloor("residence-court-" + side,
                    new Vector3(side * 13.04f, MiddleRise, .04f), Vector2.one * 10f, true));
                floors.Add(new CityTerracePilotFloor("residence-entry-" + side,
                    new Vector3(side * 13.04f, MiddleRise, -4.96f), Vector2.one * 2.4f, false));
            }
            InitializeLowerCourt(layout, floors, flights);
            foreach (CityTerracePilotFloor floor in floors)
                if (floor.HasFoundation) cuts.Add(WorldBounds(floor.Bounds));
            foreach (CityExteriorStairFlightDescriptor flight in Flights)
            {
                Rect support = CityTerracePilotPlan.FlightBounds(flight);
                // Imported treads extend .15 m beyond each walkable side.
                if (Mathf.Abs(flight.Direction.x) > .5f) { support.yMin -= .15f; support.yMax += .15f; }
                else { support.xMin -= .15f; support.xMax += .15f; }
                cuts.Add(WorldBounds(support));
            }
            cuts.Add(EntryBounds);
            cuts.Add(LowerPassBounds);
            foreach (BuildingLot lot in layout.BuildingLots)
            {
                if (!IsReservedCell(lot.Cell) || !lot.HasBuilding) continue;
                Vector3 threshold = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot)
                    .TransformPoint(CityBuildingAssetProvider.GetExpectedEntranceThreshold(lot.BuildingVariant));
                houseEntries.Add(threshold);
                float innerX = threshold.x - lot.FacadeForward.x * CityBuildingAssetProvider.EntranceDepth;
                Rect throat = Rect.MinMaxRect(Mathf.Min(innerX, threshold.x),
                    threshold.z - CityBuildingAssetProvider.EntranceClearWidth * .5f,
                    Mathf.Max(innerX, threshold.x), threshold.z + CityBuildingAssetProvider.EntranceClearWidth * .5f);
                throats.Add(throat); cuts.Add(throat);
            }
            InitializeSurfaces();
        }

        public bool IsEnabled { get; }
        public CityTerracePilotPlan Pilot { get; }
        public IReadOnlyList<Vector2Int> ReservedCells { get; }
        public Rect ReservedBounds { get; }
        public Vector3 Origin { get; }
        public Quaternion Rotation => Quaternion.Euler(0f, 90f, 0f);
        public IReadOnlyList<CityTerracePilotFloor> Floors { get; }
        public IReadOnlyList<CityExteriorStairFlightDescriptor> Flights { get; }
        public IReadOnlyList<Rect> GroundCuts { get; }
        public IReadOnlyList<Rect> HouseThroats { get; }
        public IReadOnlyList<Vector3> HouseEntryWorld { get; }
        public Vector3 EntryStartWorld { get; }
        public Vector3 EntryEndWorld { get; }
        public Rect EntryBounds { get; }
        public float EntryGradePercent { get; }
        public const string BridgeFloorId = "upper-bridge";
        public const string EastLandingFloorId = "east-street-landing";
        public const float EastLandingLength = 2.4f;
        public const float LowerPassWidth = 3f;
        public Vector3 LowerPassStartWorld { get; }
        public Vector3 LowerPassRampEndWorld { get; }
        public Vector3 LowerPassEndWorld { get; }
        public Rect LowerPassRampBounds { get; }
        public Rect LowerPassBounds { get; }
        public Rect EastLandingBounds { get; }
        public float LowerPassGradePercent { get; }
        public Rect BridgeWorldBounds { get; }
        public Vector3 UpperCrossingWorld => UpperArrivalWorld;
        public Vector3 LowerCrossingWorld => Vector3.Lerp(LowerPassStartWorld, LowerPassRampEndWorld,
            Mathf.InverseLerp(LowerPassStartWorld.x, LowerPassRampEndWorld.x, UpperCrossingWorld.x));
        public RoadEdge EntryRoadEdge => EntryEdge;
        public float EntryOpeningWidth => 10f;
        public Vector3 LowerArrivalWorld => World(new Vector3(0f, 0f, -2f));
        public Vector3 UpperArrivalWorld => World(new Vector3(0f, Pilot.TotalRise, 20.08f));
        public static Vector2Int LowerHouseCell => new Vector2Int(11, 8);
        public Rect LowerCourtBounds { get; private set; }
        public Rect LowerCourtEntryBounds { get; private set; }
        public Vector3 LowerHouseStreetWorld { get; private set; }
        public Vector3 LowerCourtEntryEndWorld { get; private set; }
        public Vector3 LowerCourtArrivalWorld { get; private set; }
        public Vector3 LowerHouseRoofWorld { get; private set; }
        public IReadOnlyList<Vector3> LowerHouseRouteWorld { get; private set; } = Array.Empty<Vector3>();
        public IReadOnlyList<RuntimeOrientedBox> LowerCourtRetainingWalls { get; private set; } = Array.Empty<RuntimeOrientedBox>();

        private void InitializeLowerCourt(CityLayout layout, List<CityTerracePilotFloor> floors,
            List<CityExteriorStairFlightDescriptor> flights)
        {
            float upper = layout.ElevationPlan.GetNodeElevation(LowerHouseCell) + CityStreetSurfacePlanner.RoadTop;
            float lower = upper - MiddleRise;
            LowerCourtBounds = Rect.MinMaxRect(135f, 57f, 146.3f, 72.2f);
            LowerCourtEntryBounds = Rect.MinMaxRect(134f, 57f, 135f, 59.4f);
            var socket = new Vector2(134f, 58.2f);
            if (!layout.ElevationPlan.TrySampleSurface(socket, CitySurfaceRole.RoadTop, out float streetTop, out _))
                throw new InvalidOperationException("The lower courtyard needs its western street socket.");
            LowerHouseStreetWorld = new Vector3(socket.x, streetTop, socket.y);
            LowerCourtEntryEndWorld = new Vector3(135f, upper, 58.2f);
            if (Mathf.Abs(upper - streetTop) * 100f > CityElevationPlan.MaximumPedestrianGradePercent + .001f)
                throw new InvalidOperationException("The lower courtyard street throat exceeds its pedestrian grade.");
            AddFloor("lower-house-top-landing", new Vector3(136.2f, upper, 58.2f), new Vector2(2.4f, 2.4f));
            AddFloor("lower-house-middle-landing", new Vector3(136.2f, upper - CityTerracePilotPlan.FlightRise, 64.44f),
                new Vector2(2.4f, 2.4f));
            AddFloor("lower-house-bottom-landing", new Vector3(136.2f, lower, 70.68f), new Vector2(2.4f, 2.4f));
            AddFloor("lower-house-court", new Vector3(141.85f, lower, 64.6f), new Vector2(8.9f, 15.2f));
            AddFlight("lower-house-flight-1", new Vector3(136.2f, upper - CityTerracePilotPlan.FlightRise, 63.24f));
            AddFlight("lower-house-flight-2", new Vector3(136.2f, lower, 69.48f));
            LowerCourtArrivalWorld = new Vector3(138.6f, lower, 70.68f);
            LowerHouseRoofWorld = new Vector3(144f, lower + 10f, 62.5f);
            LowerHouseRouteWorld = Array.AsReadOnly(new[] {
                LowerHouseStreetWorld, LowerCourtEntryEndWorld, new Vector3(136.2f, upper, 58.2f),
                new Vector3(136.2f, upper, 59.4f), new Vector3(136.2f, upper - CityTerracePilotPlan.FlightRise, 63.24f),
                new Vector3(136.2f, upper - CityTerracePilotPlan.FlightRise, 64.44f),
                new Vector3(136.2f, upper - CityTerracePilotPlan.FlightRise, 65.64f),
                new Vector3(136.2f, lower, 69.48f), new Vector3(136.2f, lower, 70.68f),
                LowerCourtArrivalWorld, new Vector3(138.6f, lower, 65f), new Vector3(141.2f, lower, 65f) });
            cuts.Add(LowerCourtBounds); cuts.Add(LowerCourtEntryBounds);
            var walls = new List<RuntimeOrientedBox>();
            float westTop = layout.ElevationPlan.GetNodeElevation(new Vector2Int(11, 8)) + CityStreetSurfacePlanner.SidewalkTop;
            float eastTop = layout.ElevationPlan.GetNodeElevation(new Vector2Int(12, 8)) + CityStreetSurfacePlanner.SidewalkTop;
            // The walls sit inside the excavation; their tops cover the old
            // terrain seam. The western entry alone stays open at street Y.
            Wall(Rect.MinMaxRect(135f, 59.4f, 135.3f, 72.2f), westTop);
            Wall(Rect.MinMaxRect(137.4f, 57f, 146.3f, 57.3f), eastTop);
            Wall(Rect.MinMaxRect(146f, 57.3f, 146.3f, 72.2f), eastTop);
            Wall(Rect.MinMaxRect(135.3f, 71.9f, 146f, 72.2f), eastTop);
            LowerCourtRetainingWalls = walls.AsReadOnly();

            void AddFloor(string id, Vector3 top, Vector2 worldSize)
            {
                floors.Add(new CityTerracePilotFloor(id, Local(top), new Vector2(worldSize.y, worldSize.x),
                    false, "TerraceCourt", .30f));
            }
            void AddFlight(string id, Vector3 start)
            {
                flights.Add(new CityExteriorStairFlightDescriptor(id, Local(start), Vector3.right,
                    CityTerracePilotPlan.FlightWidth, CityTerracePilotPlan.StepsPerFlight,
                    CityTerracePilotPlan.StepRise, CityTerracePilotPlan.TreadDepth, CityExteriorStairDropSide.Both));
            }
            void Wall(Rect bounds, float top)
            {
                float bottom = lower - .6f;
                walls.Add(new RuntimeOrientedBox(new Vector3(bounds.center.x, (top + bottom) * .5f, bounds.center.y),
                    Quaternion.identity, new Vector3(bounds.width, top - bottom, bounds.height)));
            }
        }

        public static CityTerraceDistrictPlan ForLayout(CityLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            return Plans.GetValue(layout, value => new CityTerraceDistrictPlan(value));
        }

        public bool IsReservedCell(Vector2Int cell) => IsEnabled && IsSiteCell(cell);
        public Vector3 World(Vector3 local) => Origin + new Vector3(local.z, local.y, -local.x);
        public Vector3 Local(Vector3 world)
        {
            Vector3 delta = world - Origin;
            return new Vector3(-delta.z, delta.y, delta.x);
        }

        public Rect WorldBounds(Rect local)
        {
            Vector3 first = World(new Vector3(local.xMin, 0f, local.yMin));
            Vector3 last = World(new Vector3(local.xMax, 0f, local.yMax));
            return Rect.MinMaxRect(Mathf.Min(first.x, last.x), Mathf.Min(first.z, last.z),
                Mathf.Max(first.x, last.x), Mathf.Max(first.z, last.z));
        }

        internal static bool TryReserve(CityGenerationSettings settings, Vector3 origin,
            ISet<RoadEdge> roads, ISet<int> semanticLots)
        {
            if (settings.Blueprint?.Id != CityBlueprintCatalog.DefaultBlueprintId ||
                settings.SpatialPlan?.IsCompactCoastal != true || !roads.Contains(EntryEdge) ||
                !roads.Contains(new RoadEdge(new Vector2Int(11, 7), new Vector2Int(11, 8))) ||
                !roads.Contains(new RoadEdge(new Vector2Int(11, 8), new Vector2Int(11, 9)))) return false;
            Vector2 node = settings.GetCoordinateOffset(new Vector2Int(10, 8));
            if (Mathf.Abs(origin.x + node.x - 104f) > .001f || Mathf.Abs(origin.z + node.y - 52f) > .001f) return false;
            foreach (Vector2Int cell in Cells)
                if (semanticLots.Contains(cell.y * settings.BlocksX + cell.x) || !settings.CreatesLot(cell) ||
                    !settings.Blueprint.TryGetArea(cell, out CityAreaDefinition area) || area.Archetype != CityDistrictKind.Residential)
                    return false;
            var terminal = new Vector2Int(10, 8);
            foreach (Vector2Int direction in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.down })
                if (roads.Contains(new RoadEdge(terminal, terminal + direction))) return false;
            foreach (Vector2Int cell in Cells) semanticLots.Add(cell.y * settings.BlocksX + cell.x);
            return true;
        }

        internal static void ReserveLots(List<BuildingLot> lots)
        {
            for (int index = 0; index < lots.Count; index++)
                if (IsSiteCell(lots[index].Cell)) lots[index] = SiteLot(lots[index], 0f);
        }

        internal static void RebaseReservedLots(CityElevationPlan elevation, List<BuildingLot> lots)
        {
            if (!HasReservation(lots)) return;
            float datum = elevation.GetNodeElevation(new Vector2Int(10, 8)) + CityStreetSurfacePlanner.RoadTop;
            for (int index = 0; index < lots.Count; index++)
                if (IsSiteCell(lots[index].Cell)) lots[index] = SiteLot(lots[index],
                    lots[index].Cell == LowerHouseCell
                        ? elevation.GetNodeElevation(LowerHouseCell) + CityStreetSurfacePlanner.RoadTop : datum);
        }

        private static bool IsSiteCell(Vector2Int cell) =>
            (cell.x == 9 || cell.x == 10) && (cell.y == 7 || cell.y == 8) || cell == LowerHouseCell;

        private static bool HasReservation(IReadOnlyList<BuildingLot> lots)
        {
            int ground = 0, houses = 0, lowerHouses = 0;
            foreach (BuildingLot lot in lots)
            {
                if (!IsSiteCell(lot.Cell)) continue;
                if (lot.Cell.x == 10 && lot.IsOpenGround) ground++;
                if (lot.Cell.x == 9 && lot.IsOrdinaryBuilding && lot.BuildingVariant == 1 && !lot.HasRoadFrontage) houses++;
                if (lot.Cell == LowerHouseCell && lot.IsOrdinaryBuilding && lot.BuildingVariant == 4 && !lot.HasRoadFrontage) lowerHouses++;
            }
            return ground == 2 && houses == 2 && lowerHouses == 1;
        }

        private static BuildingLot SiteLot(BuildingLot source, float datum)
        {
            if (source.Cell == LowerHouseCell)
            {
                Vector3 lowerEnvelope = CityBuildingAssetProvider.GetExpectedEnvelope(CityDistrictKind.Residential, 4);
                Vector3 lowerCenter = new Vector3(143f, datum - MiddleRise - CityFacadeGrid.MassBaseElevation, 65f);
                Vector3 lowerDoor = lowerCenter + Vector3.left * (lowerEnvelope.z * .5f);
                return new BuildingLot(source.Cell, lowerCenter, new Vector2(lowerEnvelope.z, lowerEnvelope.x),
                    lowerEnvelope.y, source.Color, source.AreaId, source.District, CityLandUseKind.Building,
                    false, false, false, string.Empty, BarActivityKind.None, Vector2Int.zero,
                    lowerDoor, lowerDoor, lowerDoor, 4, Vector3.left);
            }
            bool house = source.Cell.x == 9;
            Vector3 envelope = CityBuildingAssetProvider.GetExpectedEnvelope(CityDistrictKind.Residential, 1);
            Vector3 center = house
                ? new Vector3(HouseCenterX, datum + MiddleRise - CityFacadeGrid.MassBaseElevation,
                    OriginZ + (source.Cell.y == 7 ? -13.04f : 13.04f))
                : new Vector3(source.Center.x, datum, source.Center.z);
            Vector3 door = center + Vector3.right * (envelope.z * .5f);
            return new BuildingLot(source.Cell, center, house ? new Vector2(envelope.z, envelope.x) : source.Size,
                house ? envelope.y : .1f, source.Color, source.AreaId, source.District,
                house ? CityLandUseKind.Building : CityLandUseKind.OpenGround, false, false, false,
                string.Empty, BarActivityKind.None, Vector2Int.zero, door, door, door,
                house ? 1 : 0, Vector3.right);
        }
    }
}
