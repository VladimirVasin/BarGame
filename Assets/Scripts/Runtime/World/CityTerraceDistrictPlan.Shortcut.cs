using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityTerraceDistrictPlan
    {
        private const string ShortcutLower = "shortcut-rear-landing";
        private const string ShortcutUpper = "shortcut-street-walk";
        private const float ShortcutMouthWidth = 2.4f;
        public bool HasShortcut => ShortcutRouteWorld.Count > 0;
        public IReadOnlyList<Vector3> ShortcutRouteWorld { get; private set; } = Array.Empty<Vector3>();
        public Vector3 ShortcutCourtArrivalWorld { get; private set; }
        public Rect ShortcutBounds { get; private set; }
        internal IReadOnlyList<Rect> ShortcutFootprints { get; private set; } = Array.Empty<Rect>();
        internal IReadOnlyList<RuntimeOrientedBox> ShortcutWalls { get; private set; } = Array.Empty<RuntimeOrientedBox>();
        internal Rect ShortcutLowSocket => Socket(ShortcutRouteWorld[0]);
        internal Rect ShortcutHighSocket => Socket(ShortcutRouteWorld[ShortcutRouteWorld.Count - 1]);
        private static Rect Socket(Vector3 at) => Rect.MinMaxRect(at.x - ShortcutMouthWidth * .5f,
            at.z - .8f, at.x + ShortcutMouthWidth * .5f, at.z + .8f);

        private void InitializeShortcut(CityLayout layout, List<CityTerracePilotFloor> floors,
            List<CityExteriorStairFlightDescriptor> flights)
        {
            // Two parallel pavements. The intervening house keeps its south-facing
            // entrance; the street network only connects the two ends to the west.
            var north = new RoadEdge(new Vector2Int(11, 11), new Vector2Int(12, 11));
            var south = new RoadEdge(new Vector2Int(11, 10), new Vector2Int(12, 10));
            if (!CityCoastalAscentPlan.IsEnabled(layout) || !layout.HasRoad(north) || !layout.HasRoad(south)) return;
            // Stop 05 faces the lower street. Its roof ends at X147.00,
            // and its route sign reaches X147.605. The entire lower landing
            // starts beyond both, so the rear stair keeps its own clear mouth.
            const float backZ = 123.1f, lowX = 149.45f, highX = 155.9f, stairX = 150.9f;
            float stairEnd = stairX + CityTerracePilotPlan.FlightRun;
            var low = Rect.MinMaxRect(148.2f, 121.4f, stairX, 126f);
            var court = Rect.MinMaxRect(stairEnd, 121.2f, 157.6f, 125f);
            var walk = Rect.MinMaxRect(highX - 1.2f, 108f, highX + 1.2f, court.yMin);
            var stair = Rect.MinMaxRect(stairX, backZ - 1.05f, stairEnd, backZ + 1.05f);
            Rect[] pieces = { low, stair, court, walk };
            // A non-production seed can change the footprint/arrival. The
            // room for this particular route must actually be present.
            foreach (BuildingLot lot in layout.BuildingLots)
            {
                if (!lot.HasBuilding || lot.Cell.x < 11 || lot.Cell.x > 12 || lot.Cell.y != 10) continue;
                foreach (Rect body in lot.CreateCollisionFootprints())
                    foreach (Rect piece in pieces) if (body.Overlaps(piece)) return;
            }
            float lowY = SocketTop(north, lowX);
            float highY = SocketTop(south, highX);
            if (Mathf.Abs(highY - lowY - CityTerracePilotPlan.FlightRise) > .005f) return;
            var bottom = new Vector3(stairX, lowY, backZ);
            var top = new Vector3(stairEnd, highY, backZ);
            ShortcutCourtArrivalWorld = new Vector3(highX, highY, backZ);
            ShortcutRouteWorld = Array.AsReadOnly(new[] {
                new Vector3(lowX, lowY, low.yMax), new Vector3(lowX, lowY, backZ), bottom, top,
                ShortcutCourtArrivalWorld, new Vector3(highX, highY, court.yMin),
                new Vector3(highX, highY, walk.yMin) });
            ShortcutBounds = Rect.MinMaxRect(low.xMin, walk.yMin, court.xMax, low.yMax);
            ShortcutFootprints = Array.AsReadOnly(pieces);
            AddFloor(ShortcutLower, low, lowY, false);
            AddFloor("shortcut-upper-court", court, highY, true);
            AddFloor(ShortcutUpper, walk, highY, false);
            flights.Add(new CityExteriorStairFlightDescriptor("shortcut-rear-flight", Local(bottom),
                Vector3.forward, CityTerracePilotPlan.FlightWidth, CityTerracePilotPlan.StepsPerFlight,
                CityTerracePilotPlan.StepRise, CityTerracePilotPlan.TreadDepth, CityExteriorStairDropSide.Both));

            // Line the shallow excavation behind the rear wall, leaving
            // the northern pavement and the full clear stair throat open.
            float soil = layout.ElevationPlan.GetCellElevation(new Vector2Int(11, 10)) + CityElevationPlan.GroundTopOffset;
            if (soil > lowY)
            {
                var walls = new List<RuntimeOrientedBox>();
                Wall(Rect.MinMaxRect(low.xMin, low.yMin, low.xMin + .16f, low.yMax));
                Wall(Rect.MinMaxRect(low.xMin + .16f, low.yMin, low.xMax, low.yMin + .16f));
                Wall(Rect.MinMaxRect(low.xMax - .16f, low.yMin + .16f, low.xMax, stair.yMin));
                Wall(Rect.MinMaxRect(low.xMax - .16f, stair.yMax, low.xMax, low.yMax));
                // The flight guard is part of its FBX; these thin cheek walls
                // close the terrain edge alongside it without entering its run.
                Wall(Rect.MinMaxRect(stair.xMin, stair.yMin, stair.xMax, stair.yMin + .10f));
                Wall(Rect.MinMaxRect(stair.xMin, stair.yMax - .10f, stair.xMax, stair.yMax));
                ShortcutWalls = walls.AsReadOnly();
                void Wall(Rect r)
                {
                    float bottomY = lowY - .4f, topY = soil + .05f;
                    walls.Add(new RuntimeOrientedBox(new Vector3(r.center.x, (bottomY + topY) * .5f, r.center.y),
                        Quaternion.identity, new Vector3(r.width, topY - bottomY, r.height)));
                }
            }

            float SocketTop(RoadEdge edge, float x)
            {
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                return layout.ElevationPlan.SampleRoadDatum(edge,
                    path.Project(new Vector2(x, path.Vertices[0].y)).DistanceAlong / path.Length) +
                    CityStreetSurfacePlanner.SidewalkTop;
            }
            void AddFloor(string id, Rect r, float height, bool isCourt)
            {
                floors.Add(new CityTerracePilotFloor(id, Local(new Vector3(r.center.x, height, r.center.y)),
                    new Vector2(r.height, r.width), isCourt, "TerraceCourt", .30f));
            }
        }
    }
}
