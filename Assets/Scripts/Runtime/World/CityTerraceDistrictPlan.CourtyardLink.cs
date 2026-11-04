using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityTerraceDistrictPlan
    {
        private const float LinkHalfWidth = 1.2f;
        private const float LinkGateZ = 58.7f;
        private const string LinkEntry = "link-coast-mouth";

        public bool HasCourtyardLink => CourtyardLinkRouteWorld.Count > 0;
        public IReadOnlyList<Vector3> CourtyardLinkRouteWorld { get; private set; } = Array.Empty<Vector3>();
        public Vector3 CourtyardLinkArrivalWorld { get; private set; }
        public Rect CourtyardLinkBounds { get; private set; }
        public Rect CourtyardLinkRampBounds { get; private set; }
        public Vector3 CourtyardLinkRampStartWorld { get; private set; }
        public Vector3 CourtyardLinkRampEndWorld { get; private set; }
        internal Rect CourtyardLinkSocket => Socket(CourtyardLinkRouteWorld[0]);
        internal IReadOnlyList<Rect> CourtyardLinkFootprints { get; private set; } = Array.Empty<Rect>();
        internal IReadOnlyList<RuntimeOrientedBox> CourtyardLinkWalls { get; private set; } = Array.Empty<RuntimeOrientedBox>();

        // The alley leaves the south pavement of the upper street and rounds
        // the east side of the lower residential walk before descending to the court.
        private void InitializeCourtyardLink(CityLayout layout, List<CityTerracePilotFloor> floors,
            List<CityExteriorStairFlightDescriptor> flights)
        {
            if (!HasShortcut || !MatchesFrontage(10, 104f) || !MatchesFrontage(9, 78f)) return;
            float rise = CityTerracePilotPlan.FlightRise;
            float street = ShortcutRouteWorld[ShortcutRouteWorld.Count - 1].y;
            float window = street + rise, bottom = LowerCourtArrivalWorld.y, outlook = bottom + 4f * rise;
            var rampStart = new Vector3(162.24f, window, 89.8f);
            var rampEnd = new Vector3(162.24f, outlook, 72.6f);
            if (100f * Mathf.Abs(window - outlook) / (rampStart.z - rampEnd.z) >
                CityElevationPlan.MaximumPedestrianGradePercent) return;

            var newFloors = new List<CityTerracePilotFloor>();
            var newFlights = new List<CityExteriorStairFlightDescriptor>();
            var footprints = new List<Rect>();
            var bases = new List<float>();
            Floor(LinkEntry, street, 154.7f, 89.8f, 157.1f, 100f);
            Flight("link-window-flight", 157.1f, street, 91f, Vector3.right);
            Floor("link-window-landing", window, 160.94f, 89.8f, 163.44f, 92.2f, true);
            var rampBounds = Rect.MinMaxRect(161.04f, rampEnd.z, 163.44f, rampStart.z);
            footprints.Add(rampBounds);
            bases.Add(Mathf.Min(window, outlook));
            Floor("link-overlook", outlook, 157.1f, 70.2f, 163.44f, 72.6f, true);
            Flight("link-court-flight-1", 158.3f, bottom + 3f * rise, 66.36f, Vector3.forward);
            Floor("link-upper-turn", bottom + 3f * rise, 157.1f, 63.96f, 159.5f, 66.36f);
            Flight("link-court-flight-2", 158.3f, bottom + 2f * rise, 60.12f, Vector3.forward);
            Floor("link-house-turn", bottom + 2f * rise, 157.1f, 57.72f, 159.5f, 60.12f);
            Flight("link-court-flight-3", 153.26f, bottom + rise, 58.92f, Vector3.right);
            Floor("link-inner-turn", bottom + rise, 150.86f, 57.72f, 153.26f, 60.12f);
            Flight("link-court-flight-4", 152.06f, bottom, 63.96f, Vector3.back);
            Floor("link-bottom-turn", bottom, 147.4f, 63.96f, 153.26f, 66.36f);
            Floor("link-door-lane", bottom, 147.4f, LinkGateZ + LinkHalfWidth, 149.8f, 63.96f);
            Floor("link-court-gate", bottom, LowerCourtBounds.xMax, LinkGateZ - LinkHalfWidth,
                149.8f, LinkGateZ + LinkHalfWidth);

            foreach (BuildingLot lot in layout.BuildingLots)
            {
                if (!lot.HasBuilding || lot.Cell.x < 11 || lot.Cell.x > 12 || lot.Cell.y < 8 || lot.Cell.y > 9) continue;
                foreach (Rect body in lot.CreateCollisionFootprints())
                    foreach (Rect footprint in footprints) if (body.Overlaps(footprint)) return;
            }

            floors.AddRange(newFloors);
            flights.AddRange(newFlights);
            CourtyardLinkRampStartWorld = rampStart;
            CourtyardLinkRampEndWorld = rampEnd;
            CourtyardLinkRampBounds = rampBounds;
            cuts.Add(rampBounds);
            CourtyardLinkArrivalWorld = new Vector3(158.3f, outlook, 71.4f);
            CourtyardLinkRouteWorld = Array.AsReadOnly(new[] {
                new Vector3(155.9f, street, 100f), new Vector3(155.9f, street, 91f),
                new Vector3(157.1f, street, 91f), new Vector3(160.94f, window, 91f),
                new Vector3(162.24f, window, 91f), rampStart, rampEnd,
                new Vector3(162.24f, outlook, 71.4f), CourtyardLinkArrivalWorld,
                new Vector3(158.3f, outlook, 70.2f), new Vector3(158.3f, bottom + 3f * rise, 66.36f),
                new Vector3(158.3f, bottom + 3f * rise, 63.96f),
                new Vector3(158.3f, bottom + 2f * rise, 60.12f), new Vector3(158.3f, bottom + 2f * rise, 58.92f),
                new Vector3(157.1f, bottom + 2f * rise, 58.92f), new Vector3(153.26f, bottom + rise, 58.92f),
                new Vector3(152.06f, bottom + rise, 58.92f), new Vector3(152.06f, bottom + rise, 60.12f),
                new Vector3(152.06f, bottom, 63.96f), new Vector3(152.06f, bottom, 65.16f),
                new Vector3(148.6f, bottom, 65.16f), new Vector3(148.6f, bottom, LinkGateZ),
                new Vector3(LowerCourtBounds.xMax, bottom, LinkGateZ),
                new Vector3(LowerCourtArrivalWorld.x, bottom, LinkGateZ), LowerCourtArrivalWorld });
            CourtyardLinkFootprints = footprints.AsReadOnly();
            CourtyardLinkBounds = Rect.MinMaxRect(LowerCourtBounds.xMin, LinkGateZ - LinkHalfWidth, 163.44f, 100f);
            CourtyardLinkWalls = CreateLinkRetainingWalls(layout, footprints, bases);

            bool MatchesFrontage(int row, float z)
            {
                var node = new Vector2Int(12, row);
                return layout.HasRoad(node, node + Vector2Int.left) &&
                    (row == 10 || !layout.HasRoad(node, node + Vector2Int.right)) &&
                    !layout.HasRoad(node, node + Vector2Int.up) &&
                    !layout.HasRoad(node, node + Vector2Int.down) &&
                    (layout.RoadGeometry.GetNodePosition(node) - new Vector2(156f, z)).sqrMagnitude < .000001f;
            }
            void Floor(string id, float top, float x0, float z0, float x1, float z1, bool isCourt = false)
            {
                Rect rect = Rect.MinMaxRect(x0, z0, x1, z1);
                newFloors.Add(new CityTerracePilotFloor(id, Local(new Vector3(rect.center.x, top, rect.center.y)),
                    new Vector2(rect.height, rect.width), isCourt, "TerraceCourt", .30f));
                footprints.Add(rect);
                bases.Add(top);
            }
            void Flight(string id, float x, float y, float z, Vector3 worldDirection)
            {
                var direction = new Vector3(-worldDirection.z, 0f, worldDirection.x);
                var flight = new CityExteriorStairFlightDescriptor(id, Local(new Vector3(x, y, z)),
                    direction, CityTerracePilotPlan.FlightWidth, CityTerracePilotPlan.StepsPerFlight,
                    CityTerracePilotPlan.StepRise, CityTerracePilotPlan.TreadDepth, CityExteriorStairDropSide.Both);
                newFlights.Add(flight);
                Rect rect = WorldBounds(CityTerracePilotPlan.FlightBounds(flight));
                if (Mathf.Abs(worldDirection.x) > .5f) { rect.yMin -= .15f; rect.yMax += .15f; }
                else { rect.xMin -= .15f; rect.xMax += .15f; }
                footprints.Add(rect);
                bases.Add(y);
            }
        }

        // Line only exposed soil faces. Open seams into the next paving piece
        // and the original lower court must pass a full capsule in either direction.
        private IReadOnlyList<RuntimeOrientedBox> CreateLinkRetainingWalls(CityLayout layout,
            List<Rect> pieces, List<float> bases)
        {
            var walls = new List<RuntimeOrientedBox>();
            var neighbours = new List<Rect>(pieces) { LowerCourtBounds, CourtyardLinkSocket };
            for (int index = 0; index < pieces.Count; index++)
            {
                Rect rect = pieces[index];
                Edge(true, rect.xMin, -1f, rect.yMin, rect.yMax);
                Edge(true, rect.xMax, 1f, rect.yMin, rect.yMax);
                Edge(false, rect.yMin, -1f, rect.xMin, rect.xMax);
                Edge(false, rect.yMax, 1f, rect.xMin, rect.xMax);

                void Edge(bool alongZ, float edge, float sign, float start, float end)
                {
                    float outside = edge + sign * .02f;
                    var breaks = new List<float> { start, end };
                    foreach (Rect other in neighbours)
                    {
                        if (outside < (alongZ ? other.xMin : other.yMin) || outside > (alongZ ? other.xMax : other.yMax)) continue;
                        breaks.Add(Mathf.Clamp(alongZ ? other.yMin : other.xMin, start, end));
                        breaks.Add(Mathf.Clamp(alongZ ? other.yMax : other.xMax, start, end));
                    }
                    breaks.Sort();
                    for (int part = 1; part < breaks.Count; part++)
                    {
                        float from = breaks[part - 1], to = breaks[part], length = to - from;
                        if (length < .025f) continue;
                        float mid = (from + to) * .5f;
                        Vector2 probe = alongZ ? new Vector2(outside, mid) : new Vector2(mid, outside);
                        if (neighbours.Exists(neighbour => neighbour.Contains(probe)) ||
                            !layout.ElevationPlan.TrySampleSurface(probe, CitySurfaceRole.GroundTop, out float soil, out _) ||
                            soil < bases[index] + .12f) continue;
                        float wallBottom = bases[index] - .4f, top = soil + .12f;
                        var center = alongZ ? new Vector2(edge - sign * .06f, mid) : new Vector2(mid, edge - sign * .06f);
                        walls.Add(new RuntimeOrientedBox(new Vector3(center.x, (top + wallBottom) * .5f, center.y),
                            Quaternion.identity, new Vector3(alongZ ? .12f : length, top - wallBottom, alongZ ? length : .12f)));
                    }
                }
            }
            return walls.AsReadOnly();
        }
    }
}
