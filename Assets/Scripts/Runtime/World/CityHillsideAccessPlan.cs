using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The same surveyed landings and flights support the model, capsule, map and pedestrian graph.</summary>
    public sealed class CityHillsideShortcut
    {
        public const float Width = 2.1f;
        private const float LandingLength = 1.5f;
        private const float Curb = CityStreetSurfacePlanner.SidewalkTop - CityStreetSurfacePlanner.RoadTop;
        public CityTerraceTransportBank Bank { get; }
        public int LowerLane { get; }
        public string Id { get; }
        public Rect Bounds { get; private set; }
        public IReadOnlyList<Rect> WalkableBounds => bounds;
        public IReadOnlyList<Vector3> RouteWorld => route;
        public IReadOnlyList<CityTerracePilotFloor> Floors => floors;
        public IReadOnlyList<CityExteriorStairFlightDescriptor> Flights => flights;

        private readonly List<Vector3> route = new();
        private readonly List<Rect> bounds = new();
        private readonly List<CityTerracePilotFloor> floors = new();
        private readonly List<CityExteriorStairFlightDescriptor> flights = new();
        private Vector3 Cursor => route[route.Count - 1];

        private CityHillsideShortcut(CityTerraceTransportBank bank, int lane, string id, Vector3 start)
        {
            Bank = bank;
            LowerLane = lane;
            Id = id;
            route.Add(start);
        }

        public bool TrySampleTop(Vector2 point, out float top)
        {
            top = 0f;
            if (!CityTerracePilotPlan.Inside(Bounds, point.x, point.y)) return false;
            foreach (var floor in floors)
                if (CityTerracePilotPlan.Inside(floor.Bounds, point.x, point.y))
                {
                    top = floor.Center.y;
                    return true;
                }
            foreach (var flight in flights)
            {
                if (!CityTerracePilotPlan.Inside(FlightFootprint(flight), point.x, point.y)) continue;
                float run = Vector3.Dot(new Vector3(point.x, flight.Start.y, point.y) - flight.Start, flight.Direction);
                top = flight.BaseElevation + Mathf.Clamp01(run / flight.RunLength) * flight.TotalRise;
                return true;
            }
            return false;
        }

        internal static CityHillsideShortcut Create(CityHillsideStreetRoute street, int lower)
        {
            // Vary the number of modules so adjacent stairs meet different parts of the street.
            int count = lower == 0 || lower == 3 ? 1 : lower == 2 || lower == 4 ? 3 : 2;
            float north = street.GetLaneZ(lower) - CityHillsideStreetRoute.OuterHalfWidth;
            float south = street.GetLaneZ(lower + 1) + CityHillsideStreetRoute.OuterHalfWidth;
            float left = street.LaneMinX + 9f, right = street.LaneMaxX - 9f;
            if (lower == 0)
            {
                // The first street begins at the quarter-turn tangent, not at the full seven-lane extent.
                float actualMin = float.PositiveInfinity, actualMax = float.NegativeInfinity;
                foreach (var point in street.Path.Vertices)
                    if (Mathf.Abs(point.y - street.GetLaneZ(0)) < .001f)
                    {
                        actualMin = Mathf.Min(actualMin, point.x);
                        actualMax = Mathf.Max(actualMax, point.x);
                    }
                left = Mathf.Max(left, actualMin + 9f);
                right = Mathf.Min(right, actualMax - 9f);
            }
            if (left >= right) throw new InvalidOperationException("Hillside stair has no shared straight frontage.");

            float target = count * CityTerracePilotPlan.FlightRise;
            float a = Delta(left), b = Delta(right);
            float x;
            if (a * b > 0f) x = Mathf.Abs(a) < Mathf.Abs(b) ? left : right;
            else
            {
                for (int i = 0; i < 22; i++)
                {
                    float mid = (left + right) * .5f;
                    if (a * Delta(mid) <= 0f) right = mid;
                    else left = mid;
                }
                x = (left + right) * .5f;
            }
            float low = street.SampleLaneTop(lower, x) + Curb;
            float rise = street.SampleLaneTop(lower + 1, x) + Curb - low;
            var stair = new CityHillsideShortcut(street.Bank, lower, street.Bank + "-" + lower, new Vector3(x, low, north));
            float landing = count == 2 ? CityTerracePilotPlan.LandingSize : LandingLength;
            float apron = (north - south - count * CityTerracePilotPlan.FlightRun - (count - 1) * landing) * .5f;
            stair.Flat(Vector3.back, apron);
            stair.FlightRun(Vector3.back, count, rise / count, landing);
            stair.Flat(Vector3.back, apron);
            return stair;

            float Delta(float at) => street.SampleLaneTop(lower + 1, at) - street.SampleLaneTop(lower, at) - target;
        }

        internal static CityHillsideShortcut CreateShared(CityHillsideQuarterPlan quarter)
        {
            if (quarter.Links.Count == 0) return null;
            var court = quarter.Links[0];
            var summit = quarter.Links[1];
            const float x = 8f, cornerZ = -327f, width = CityTerracePilotPlan.LandingSize;
            float half = width * .5f;
            float north = court.WestGateWorld.z - CityHillsideStreetRoute.OuterHalfWidth;
            float south = summit.WestGateWorld.z + CityHillsideStreetRoute.OuterHalfWidth;
            var stair = new CityHillsideShortcut(default, -1, "shared-court",
                new Vector3(x, court.PavementTopAt(x), north));
            // The flat entry is a through passage on the lower datum. Only beyond
            // the back wall do the stairs turn east, then south to the high street.
            stair.Flat(Vector3.back, north - cornerZ - half, width);
            stair.Turn(Vector3.back, Vector3.right, width);
            float rise = summit.PavementTopAt(x) - stair.Cursor.y; // the summit link is level
            int count = Mathf.RoundToInt(rise / CityTerracePilotPlan.FlightRise);
            if (count < 3 || count > 6) throw new InvalidOperationException("The shared court needs a new flight layout for this elevation.");
            float flightRise = rise / count;
            stair.FlightRun(Vector3.right, count - 2, flightRise, LandingLength);
            stair.Turn(Vector3.right, Vector3.back, width);
            stair.FlightRun(Vector3.back, 2, flightRise, LandingLength);
            stair.Flat(Vector3.back, stair.Cursor.z - south, width);
            return stair;
        }

        internal static CityHillsideShortcut CreateTransfer(CityLayout layout, CityHillsideStreetRoute street)
        {
            // Both public approaches open beside their shelters. Survey the corridor
            // from the two streets before bus furnishing or pedestrian routing exists.
            bool west = street.Bank == CityTerraceTransportBank.West;
            float x = west ? -99f : 84f;
            float roadZ = layout.RoadGeometry.GetNodePosition(street.MouthNode).y;
            float bottomZ = roadZ, bottomY = 0f;
            foreach (var patch in CityStreetSupportPlan.ForLayout(layout).Patches)
            {
                float edge = patch.Bounds.yMin;
                if (patch.Kind != CityStreetSupportKind.Sidewalk || edge >= bottomZ ||
                    edge < roadZ - layout.RoadWidth || x < patch.Bounds.xMin || x > patch.Bounds.xMax) continue;
                if (patch.TrySampleTop(new Vector2(x, edge), out float height))
                {
                    bottomZ = edge;
                    bottomY = height;
                }
            }
            if (bottomZ >= roadZ) throw new InvalidOperationException("Missing lower transfer pavement on " + street.Bank);
            float topZ = street.GetLaneZ(0) + CityHillsideStreetRoute.OuterHalfWidth;
            if (!street.TrySampleTop(new Vector2(x, topZ), out float topY, out var kind) ||
                kind != CityStreetSupportKind.Sidewalk)
                throw new InvalidOperationException("Missing upper transfer pavement on " + street.Bank);

            float rise = topY - bottomY;
            int count = Mathf.Max(1, Mathf.RoundToInt(rise / CityTerracePilotPlan.FlightRise),
                Mathf.CeilToInt(rise / (CityTerracePilotPlan.StepsPerFlight * .18f)));
            float run = count * CityTerracePilotPlan.FlightRun + (count - 1) * LandingLength;
            float bankEdge = CityHillsideNeighborhoodPlan.TerrainBounds(street).yMax;
            float flat = bottomZ - topZ - run;
            const float minimumApron = 1.2f;
            if (rise <= 0f || flat < 2f * minimumApron)
                throw new InvalidOperationException($"Transfer on {street.Bank}: lower ({x},{bottomY},{bottomZ}), " +
                    $"upper ({x},{topY},{topZ}), rise {rise}, {count} flights, {flat} m for both aprons.");
            // The steeper east bank needs its first flight on the forecourt
            // below the old soil seam. Both sidewalk entries stay level.
            float upperApron = Mathf.Clamp((bankEdge - topZ - run) * .5f,
                minimumApron, flat - minimumApron);
            var stair = new CityHillsideShortcut(street.Bank, -1, west ? "transfer-west" : "transfer-east",
                new Vector3(x, bottomY, bottomZ));
            stair.Flat(Vector3.back, flat - upperApron);
            stair.FlightRun(Vector3.back, count, rise / count, LandingLength);
            stair.Flat(Vector3.back, upperApron);
            return stair;
        }

        private void FlightRun(Vector3 direction, int count, float rise, float landing)
        {
            for (int i = 0; i < count; i++)
            {
                if (i > 0) Flat(direction, landing);
                var flight = new CityExteriorStairFlightDescriptor(Id + " flight " + flights.Count, Cursor, direction,
                    CityTerracePilotPlan.FlightWidth, CityTerracePilotPlan.StepsPerFlight,
                    rise / CityTerracePilotPlan.StepsPerFlight, CityTerracePilotPlan.TreadDepth, CityExteriorStairDropSide.Both);
                flights.Add(flight);
                AddBounds(FlightFootprint(flight));
                route.Add(flight.End);
            }
        }

        private void Flat(Vector3 direction, float length, float width = Width)
        {
            if (length <= 0f) throw new InvalidOperationException("Stair landing has no run at " + Id);
            Vector3 end = Cursor + direction * length;
            Vector2 size = direction.x == 0f ? new Vector2(width, length) : new Vector2(length, width);
            AddFloor((Cursor + end) * .5f, size);
            route.Add(end);
        }

        private void Turn(Vector3 incoming, Vector3 outgoing, float width)
        {
            Vector3 center = Cursor + incoming * (width * .5f);
            AddFloor(center, Vector2.one * width);
            route.Add(center);
            route.Add(center + outgoing * (width * .5f));
        }

        private void AddFloor(Vector3 center, Vector2 size)
        {
            var floor = new CityTerracePilotFloor(Id + " landing " + floors.Count, center, size, true);
            floors.Add(floor);
            AddBounds(floor.Bounds);
        }

        private static Rect FlightFootprint(CityExteriorStairFlightDescriptor flight)
        {
            Vector3 mid = (flight.Start + flight.End) * .5f;
            Vector2 size = flight.Direction.x == 0f ? new Vector2(Width, flight.RunLength) : new Vector2(flight.RunLength, Width);
            return new Rect(new Vector2(mid.x, mid.z) - size * .5f, size);
        }

        private void AddBounds(Rect piece)
        {
            Bounds = bounds.Count == 0 ? piece : Rect.MinMaxRect(
                Mathf.Min(Bounds.xMin, piece.xMin), Mathf.Min(Bounds.yMin, piece.yMin),
                Mathf.Max(Bounds.xMax, piece.xMax), Mathf.Max(Bounds.yMax, piece.yMax));
            bounds.Add(piece);
        }
    }

    public sealed class CityHillsideAccessPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityHillsideAccessPlan> Plans = new();
        public IReadOnlyList<CityHillsideShortcut> Shortcuts { get; }
        public IReadOnlyList<CityHillsideShortcut> Transfers { get; }
        public CityHillsideShortcut SharedCourt { get; }
        public IReadOnlyList<CityHillsideShortcut> AllStairs { get; }
        private CityHillsideAccessPlan(CityLayout layout)
        {
            var slopes = new List<CityHillsideShortcut>();
            var transfers = new List<CityHillsideShortcut>(2);
            foreach (CityHillsideStreetRoute street in CityHillsideStreetPlan.ForLayout(layout).Routes)
            {
                for (int lane = 0; lane + 1 < CityHillsideStreetRoute.LaneCount; lane++)
                    slopes.Add(CityHillsideShortcut.Create(street, lane));
                transfers.Add(CityHillsideShortcut.CreateTransfer(layout, street));
            }
            Shortcuts = slopes.AsReadOnly();
            Transfers = transfers.AsReadOnly();
            SharedCourt = CityHillsideShortcut.CreateShared(CityHillsideQuarterPlan.ForLayout(layout));
            var all = new List<CityHillsideShortcut>(slopes);
            all.AddRange(transfers);
            if (SharedCourt != null) all.Add(SharedCourt);
            AllStairs = all.AsReadOnly();
        }
        public static CityHillsideAccessPlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityHillsideAccessPlan(value));
    }
}
