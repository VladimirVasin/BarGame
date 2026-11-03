using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The first occupied terrace precinct. All geometry is local to Origin;
    /// reservations, cuts, entrance contacts and height queries use world metres.</summary>
    public sealed class CityTerraceDistrictPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityTerraceDistrictPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityTerraceDistrictPlan>();
        private static readonly Vector2Int[] Cells = {
            new Vector2Int(9, 7), new Vector2Int(10, 7),
            new Vector2Int(9, 8), new Vector2Int(10, 8) };
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
            Flights = Pilot.Flights;
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

            foreach (int side in new[] { -1, 1 })
            {
                floors.Add(new CityTerracePilotFloor("residence-court-" + side,
                    new Vector3(side * 13.04f, MiddleRise, .04f), Vector2.one * 10f, true));
                floors.Add(new CityTerracePilotFloor("residence-entry-" + side,
                    new Vector3(side * 13.04f, MiddleRise, -4.96f), Vector2.one * 2.4f, false));
            }
            foreach (CityTerracePilotFloor floor in floors) cuts.Add(WorldBounds(floor.Bounds));
            foreach (CityExteriorStairFlightDescriptor flight in Flights)
            {
                Rect support = CityTerracePilotPlan.FlightBounds(flight);
                // Imported treads extend .15 m beyond each walkable side.
                if (Mathf.Abs(flight.Direction.x) > .5f) { support.yMin -= .15f; support.yMax += .15f; }
                else { support.xMin -= .15f; support.xMax += .15f; }
                cuts.Add(WorldBounds(support));
            }
            cuts.Add(EntryBounds);
            foreach (BuildingLot lot in layout.BuildingLots)
            {
                if (!IsReservedCell(lot.Cell) || !lot.HasBuilding) continue;
                Vector3 threshold = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot)
                    .TransformPoint(CityBuildingAssetProvider.GetExpectedEntranceThreshold(lot.BuildingVariant));
                houseEntries.Add(threshold);
                Rect throat = Rect.MinMaxRect(threshold.x - CityBuildingAssetProvider.EntranceDepth,
                    threshold.z - CityBuildingAssetProvider.EntranceClearWidth * .5f,
                    threshold.x, threshold.z + CityBuildingAssetProvider.EntranceClearWidth * .5f);
                throats.Add(throat); cuts.Add(throat);
            }
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
        public RoadEdge EntryRoadEdge => EntryEdge;
        public float EntryOpeningWidth => 10f;
        public Vector3 LowerArrivalWorld => World(new Vector3(0f, 0f, -2f));
        public Vector3 UpperArrivalWorld => World(new Vector3(0f, Pilot.TotalRise, 20.08f));

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

        public bool ContainsFootprint(Vector2 point)
        {
            if (!IsEnabled) return false;
            foreach (Rect cut in cuts) if (CityTerracePilotPlan.Inside(cut, point.x, point.y)) return true;
            return false;
        }

        public bool TryGetSurfaceHeight(Vector3 world, out float height) =>
            TrySampleStandingTop(new Vector2(world.x, world.z), 0f, out height);

        public bool TrySampleStandingTop(Vector2 point, float radius, out float height)
        {
            height = 0f;
            if (!IsEnabled || !CityTerracePilotPlan.Finite(radius) || radius < 0f || !Sample(point, out height)) return false;
            if (radius <= .0001f) return true;
            return Sample(point + Vector2.right * radius, out _) && Sample(point - Vector2.right * radius, out _) &&
                Sample(point + Vector2.up * radius, out _) && Sample(point - Vector2.up * radius, out _);
        }

        private bool Sample(Vector2 point, out float height)
        {
            height = 0f;
            if (!CityTerracePilotPlan.Finite(point.x) || !CityTerracePilotPlan.Finite(point.y)) return false;
            if (CityTerracePilotPlan.Inside(EntryBounds, point.x, point.y))
            {
                height = Mathf.Lerp(EntryStartWorld.y, EntryEndWorld.y,
                    Mathf.InverseLerp(EntryStartWorld.x, EntryEndWorld.x, point.x));
                return true;
            }
            foreach (Rect throat in throats)
                if (CityTerracePilotPlan.Inside(throat, point.x, point.y))
                { height = Origin.y + MiddleRise; return true; }
            Vector3 local = Local(new Vector3(point.x, 0f, point.y));
            bool found = Pilot.TryGetSurfaceHeight(local, out float top);
            for (int index = Pilot.Floors.Count; index < Floors.Count; index++)
            {
                CityTerracePilotFloor floor = Floors[index];
                if (!CityTerracePilotPlan.Inside(floor.Bounds, local.x, local.z)) continue;
                top = found ? Mathf.Max(top, floor.Center.y) : floor.Center.y;
                found = true;
            }
            if (found) height = Origin.y + top;
            return found;
        }

        internal static bool TryReserve(CityGenerationSettings settings, Vector3 origin,
            ISet<RoadEdge> roads, ISet<int> semanticLots)
        {
            if (settings.Blueprint?.Id != CityBlueprintCatalog.DefaultBlueprintId ||
                settings.SpatialPlan?.IsCompactCoastal != true || !roads.Contains(EntryEdge)) return false;
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
                if (IsSiteCell(lots[index].Cell)) lots[index] = SiteLot(lots[index], datum);
        }

        private static bool IsSiteCell(Vector2Int cell) =>
            (cell.x == 9 || cell.x == 10) && (cell.y == 7 || cell.y == 8);

        private static bool HasReservation(IReadOnlyList<BuildingLot> lots)
        {
            int ground = 0, houses = 0;
            foreach (BuildingLot lot in lots)
            {
                if (!IsSiteCell(lot.Cell)) continue;
                if (lot.Cell.x == 10 && lot.IsOpenGround) ground++;
                if (lot.Cell.x == 9 && lot.IsOrdinaryBuilding && lot.BuildingVariant == 1 && !lot.HasRoadFrontage) houses++;
            }
            return ground == 2 && houses == 2;
        }

        private static BuildingLot SiteLot(BuildingLot source, float datum)
        {
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
