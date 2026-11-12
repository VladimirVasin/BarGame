using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static partial class CityPedestrianPlanner
    {
        private static void BuildHillsidePaths(CityLayout layout, CityStreetSurfacePlan streets, GraphBuilder graph)
        {
            var access = CityHillsideAccessPlan.ForLayout(layout);
            var district = CityHillsideNeighborhoodPlan.ForLayout(layout);
            var hillside = CityHillsideStreetPlan.ForLayout(layout);
            if (hillside.Routes.Count == 0) return;
            CityBusPlan hillLine = null;
            foreach (var line in CityDistrictBusPlanner.CreateRoadRouting(layout))
                if (line.Number == 3) hillLine = line;
            foreach (var road in hillside.Routes)
            {
                AddPavedShoulders(road.SurfacePatches);
                string prefix = "hill:" + road.Bank + ":";
                string pavement = prefix + "pavement:";
                int right = AddPavement(road.SidewalkPath, "right");
                int left = AddPavement(road.OppositeSidewalkPath, "left");
                // House and stair gates may lie across the street or beyond the
                // walker's seven-metre final approach. Cut the actual boarding-side
                // footway at its platform, retaining both surrounding pavement legs.
                if (hillLine != null)
                    foreach (var stop in hillLine.Stops)
                    {
                        if (!road.TrySampleTop(XZ(stop.Position), out _, out var surface) || surface != CityStreetSupportKind.Asphalt) continue;
                        var curb = CityBusStopWaitPlanner.StandingPosition(hillLine, stop, layout);
                        string side = road.Bank == CityTerraceTransportBank.West ? "right" : "left";
                        Gate(prefix + "bus:" + stop.SequenceIndex, pavement + side, curb);
                    }
                Connect(prefix + "summit-crossing", right, left, CityPedestrianLinkKind.Crosswalk, StreetHeight);
                foreach (var stair in access.Shortcuts)
                    if (stair.Bank == road.Bank) AddStair(stair, prefix + "stair:" + stair.LowerLane, pavement, pavement);
                foreach (var house in district.Buildings)
                    if (house.Bank == road.Bank) AddHouse(house, prefix, pavement);

                int AddPavement(IReadOnlyList<Vector3> points, string side)
                {
                    int street = graph.SplitTerraceStreetAt(prefix + side + ":mouth", XZ(points[0]), StreetHeight);
                    int start = JoinPoint(prefix + side + ":from", street, points[0], StreetHeight);
                    int end = graph.AddNode(prefix + side + ":to", points[points.Count - 1], false);
                    AddPath(pavement + side, start, end, points);
                    return end;
                }
            }

            foreach (var link in CityHillsideQuarterPlan.ForLayout(layout).Links)
            {
                AddPavedShoulders(link.SurfacePatches);
                string prefix = "hill:quarter:" + link.Id + ":";
                AddSide(link.NorthSidewalk, "north");
                AddSide(link.SouthSidewalk, "south");

                void AddSide(IReadOnlyList<Vector3> points, string side)
                {
                    string id = prefix + side;
                    int west = Gate(id + ":west", "hill:West:pavement:", points[0]);
                    int east = Gate(id + ":east", "hill:East:pavement:", points[points.Count - 1]);
                    AddPath(id, west, east, points);
                }
            }

            if (access.SharedCourt != null)
                AddStair(access.SharedCourt, "hill:quarter:passage", "hill:quarter:court:south", "hill:quarter:summit:north");
            foreach (var house in district.SharedCourtBuildings)
            {
                string prefix = house.OnSummitStreet ? "hill:quarter:summit:" : "hill:quarter:court:";
                AddHouse(house, prefix, prefix + (house.ThresholdWorld.z > house.StreetContactWorld.z ? "north" : "south"));
            }
            if (RidgeKioskSitePlan.TryCreate(layout, out var kiosk) && kiosk.Court != null)
                AddKioskCourt(kiosk.Court);
            foreach (var transfer in access.Transfers)
            {
                string id = "hill:" + transfer.Id;
                int lower = graph.SplitTerraceStreetAt(id + ":lower", XZ(transfer.RouteWorld[0]), StreetHeight);
                string upperPavement = transfer.Bank == CityTerraceTransportBank.West ?
                    "hill:West:pavement:left" : "hill:East:pavement:right";
                AddStairFrom(lower, transfer, id, upperPavement);
            }

            // Route centrelines guide the feet. For passing, expose the actual
            // paved band, including its widened bends, without admitting traffic lanes.
            void AddPavedShoulders(IReadOnlyList<CityStreetSupportPatch> patches)
            {
                foreach (var patch in patches)
                {
                    if (!patch.IsSupport || patch.Kind != CityStreetSupportKind.Sidewalk) continue;
                    var polygon = new Vector2[patch.TopVertices.Count];
                    for (int i = 0; i < polygon.Length; i++) polygon[i] = XZ(patch.TopVertices[i]);
                    graph.AddNavigationPolygon(CityRoadPolygon.CounterClockwise(polygon));
                }
            }

            int Gate(string id, string pavement, Vector3 point) =>
                graph.SplitHillsideLane(id, pavement, XZ(point), StreetHeight);

            float StreetHeight(Vector2 point)
            {
                if (streets.SupportPlan.TrySampleTop(point, out float y, out _)) return y;
                throw new InvalidOperationException("Hillside pavement has no support at " + point);
            }

            void AddPath(string id, int from, int to, IReadOnlyList<Vector3> points)
            {
                var shape = new List<Vector2>(points.Count);
                foreach (var point in points) shape.Add(XZ(point));
                graph.AddLink(id, from, to, CityPedestrianLinkKind.Sidewalk, true, new CityRoadPath(shape), StreetHeight);
            }

            void Connect(string id, int from, int to, CityPedestrianLinkKind kind, Func<Vector2, float> height)
            {
                if (from == to) return;
                Vector2 a = XZ(graph.Nodes[from].Position), b = XZ(graph.Nodes[to].Position);
                graph.AddLink(id, from, to, kind, false, new CityRoadPath(new[] { a, b }), height);
            }

            int JoinPoint(string id, int from, Vector3 point, Func<Vector2, float> height,
                CityPedestrianLinkKind kind = CityPedestrianLinkKind.Sidewalk)
            {
                if ((XZ(graph.Nodes[from].Position) - XZ(point)).sqrMagnitude < .000001f) return from;
                int next = graph.AddNode(id, point, false);
                Connect(id + ":walk", from, next, kind, height);
                return next;
            }

            void AddStair(CityHillsideShortcut stair, string id, string lowerPavement, string upperPavement)
            {
                int lower = Gate(id + ":lower", lowerPavement, stair.RouteWorld[0]);
                AddStairFrom(lower, stair, id, upperPavement);
            }

            void AddStairFrom(int previous, CityHillsideShortcut stair, string id, string upperPavement)
            {
                foreach (var floor in stair.Floors)
                    graph.AddNavigationPolygon(CityRoadPolygon.Rectangle(floor.Bounds));
                foreach (var flight in stair.Flights)
                {
                    Vector3 side = flight.Right * (flight.Width * .5f);
                    graph.AddNavigationPolygon(CityRoadPolygon.CounterClockwise(new[] {
                        XZ(flight.Start - side), XZ(flight.Start + side),
                        XZ(flight.End + side), XZ(flight.End - side) }));
                }
                var points = stair.RouteWorld;
                for (int i = 0; i < points.Count; i++)
                    previous = JoinPoint(id + ":" + i, previous, points[i], Height, CityPedestrianLinkKind.Stair);
                int upper = Gate(id + ":upper", upperPavement, points[points.Count - 1]);
                Connect(id + ":upper-join", previous, upper, CityPedestrianLinkKind.Stair, Height);
                float Height(Vector2 p) => stair.TrySampleTop(p, out float y) ? y : StreetHeight(p);
            }

            void AddHouse(CityHillsideBuilding house, string prefix, string pavement)
            {
                string id = prefix + "house:" + house.Lot.Cell.x + ":" + house.Lot.Cell.y;
                Rect court = house.CourtBounds;
                Vector2 center = court.center;
                float clearance = 2f * (AgentRadius + NavigationMargin);
                if (court.width <= clearance || court.height <= clearance) return;
                int gate = Gate(id + ":gate", pavement, house.StreetContactWorld);
                // The frontage stays public. The hero is the sole occupant
                // and only the player's exterior ground leads to this door.
                if (house.Lot.IsPlayerHome) return;

                // Use the laid pavement for side steps as well as the route
                // centre. Posts and railings still constrain live clearance.
                graph.AddNavigationPolygon(CityRoadPolygon.Rectangle(house.ApproachBounds));
                graph.AddNavigationPolygon(CityRoadPolygon.Rectangle(court));
                const CityPedestrianLinkKind kind = CityPedestrianLinkKind.HomeEntrance;
                int start = JoinPoint(id + ":street", gate, house.StreetContactWorld, Height, kind);
                // The long axis of a gable-facing entrance is offset from its porch centre.
                // Stay on the apron until reaching the court.
                Vector3 elbow = new Vector3(house.StreetContactWorld.x, house.DoorWorld.y, center.y);
                int approach = JoinPoint(id + ":apron", start, elbow, Height, kind);
                int entry = JoinPoint(id + ":entry", approach, GroundedPoint(center, Height), Height, kind);
                graph.RetainEntranceTerminal(entry);

                float Height(Vector2 p)
                {
                    // The first metre belongs to the physical sidewalk where
                    // its grade may differ from the house's lane survey.
                    if (streets.SupportPlan.TrySampleTop(p, out float y, out _)) return y;
                    return house.TrySampleApproach(p, out y) ? y : StreetHeight(p);
                }
            }

            void AddKioskCourt(RidgeKioskCourtPlan court)
            {
                const string prefix = RidgeKioskCourtPlan.NodePrefix;
                Vector3 street = court.Point(court.SideAisle, CityKioskSitePlan.ApronFront + 1f);
                var summit = CityHillsideQuarterPlan.ForLayout(layout).Links[1];
                string pavement = street.x >= summit.Bounds.xMin + .10f ? "hill:quarter:summit:south" : "hill:West:pavement:";
                int gate = Gate(prefix + "gate", pavement, street);
                foreach (Rect floor in court.WalkableFloors) graph.AddNavigationPolygon(CityRoadPolygon.Rectangle(floor));
                int entry = Join(gate, "entry", court.Entry);
                int windowTurn = Join(entry, "window-turn", court.Point(court.EntrySide * 1.94f, 2.48f));
                int window = Join(windowTurn, "window", court.Window, CityPedestrianLinkKind.Courtyard);
                graph.RetainEntranceTerminal(window);
                float frontZ = RidgeKioskCourtPlan.FrontAisle;
                int side = Join(entry, "side-aisle", court.Point(court.SideAisle, frontZ));

                // The waiting pockets are dead ends outside the through aisle.
                // Each entrance spur runs normal to the aisle, clear of the next pocket.
                var frontXs = new SortedSet<float> { court.Entry.x, court.Table(0).x, court.Origin.x, court.Table(1).x };
                for (int slot = 0; slot < RidgeKioskCourtPlan.Capacity; slot++) frontXs.Add(court.Wait(slot).x);
                var front = new Dictionary<float, int> { { court.Entry.x, side } };
                int index = 0, last = side;
                foreach (float x in frontXs)
                    if (x > court.Entry.x) last = AddFront(last, x);
                last = side;
                foreach (float x in frontXs.Reverse())
                    if (x < court.Entry.x) last = AddFront(last, x);
                for (int slot = 0; slot < RidgeKioskCourtPlan.Capacity; slot++)
                {
                    Vector3 wait = court.Wait(slot);
                    int stop = JoinPoint(RidgeKioskCourtPlan.WaitingId(slot), front[wait.x],
                        GroundedPoint(XZ(wait), Height), Height, CityPedestrianLinkKind.Courtyard);
                    graph.RetainEntranceTerminal(stop);
                }

                int leftBend = Join(front[court.Table(0).x], "left-bend", court.Point(-3.35f, -3.84f));
                int leftOuter = Join(leftBend, "left-edge", court.Point(-4.22f, -3.84f));
                leftOuter = Join(leftOuter, "left-outer", court.Point(-4.22f, -4.94f));
                int rightBend = Join(front[court.Table(1).x], "right-bend", court.Point(4.20f, -3.20f));
                int rightOuter = Join(rightBend, "right-outer", court.Point(4.20f, -4.94f));
                int centre = Join(front[court.Origin.x], "centre-aisle", court.Point(0f, -4.94f));
                int back = Join(centre, "back-aisle", court.Point(0f, RidgeKioskCourtPlan.BackAisle));
                int backLeft = Join(back, "rear-left", court.Point(-2.0f, RidgeKioskCourtPlan.BackAisle));
                int backRight = Join(back, "rear-right", court.Point(2.0f, RidgeKioskCourtPlan.BackAisle));
                backLeft = Join(backLeft, "rear-left-edge", court.Point(-2.0f, -8.43f));
                backRight = Join(backRight, "rear-right-edge", court.Point(2.0f, -8.43f));
                var accesses = new[] { leftOuter, centre, centre, rightOuter, backLeft, backRight };
                for (int slot = 0; slot < accesses.Length; slot++)
                {
                    int place = Join(accesses[slot], "place-" + slot, court.Place(slot), CityPedestrianLinkKind.Courtyard);
                    graph.RetainEntranceTerminal(place);
                }

                int AddFront(int from, float x)
                {
                    int node = Join(from, "front-" + index++, new Vector3(x, court.Origin.y, court.Origin.z + frontZ));
                    front.Add(x, node);
                    return node;
                }

                // Reserved visitors use these spurs. Ordinary roaming never
                // enters them, and the public graph keeps the through sidewalk.
                int Join(int from, string name, Vector3 point, CityPedestrianLinkKind kind = CityPedestrianLinkKind.HomeEntrance) =>
                    JoinPoint(prefix + name, from, GroundedPoint(XZ(point), Height), Height, kind);

                float Height(Vector2 p)
                {
                    if (CityRoadPolygon.Contains(kiosk.ApronPolygons[0], p)) return kiosk.Origin.y;
                    if (court.TrySurface(p, out float top)) return top;
                    return StreetHeight(p);
                }
            }
        }

        private sealed partial class GraphBuilder
        {
            internal int SplitHillsideLane(string id, string prefix, Vector2 xz, Func<Vector2, float> height) =>
                SplitWalkingPathAt(id, CityPedestrianLinkKind.Sidewalk, prefix, xz, height, 1.2f * 1.2f);
        }
    }
}
