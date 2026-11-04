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
            foreach (var transfer in access.Transfers)
            {
                string id = "hill:" + transfer.Id;
                int lower = graph.SplitTerraceStreetAt(id + ":lower", XZ(transfer.RouteWorld[0]), StreetHeight);
                string upperPavement = transfer.Bank == CityTerraceTransportBank.West ?
                    "hill:West:pavement:left" : "hill:East:pavement:right";
                AddStairFrom(lower, transfer, id, upperPavement);
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

            int JoinPoint(string id, int from, Vector3 point, Func<Vector2, float> height)
            {
                if ((XZ(graph.Nodes[from].Position) - XZ(point)).sqrMagnitude < .000001f) return from;
                int next = graph.AddNode(id, point, false);
                Connect(id + ":walk", from, next, CityPedestrianLinkKind.Sidewalk, height);
                return next;
            }

            void AddStair(CityHillsideShortcut stair, string id, string lowerPavement, string upperPavement)
            {
                int lower = Gate(id + ":lower", lowerPavement, stair.RouteWorld[0]);
                AddStairFrom(lower, stair, id, upperPavement);
            }

            void AddStairFrom(int previous, CityHillsideShortcut stair, string id, string upperPavement)
            {
                var points = stair.RouteWorld;
                for (int i = 0; i < points.Count; i++)
                    previous = JoinPoint(id + ":" + i, previous, points[i], Height);
                int upper = Gate(id + ":upper", upperPavement, points[points.Count - 1]);
                Connect(id + ":upper-join", previous, upper, CityPedestrianLinkKind.Sidewalk, Height);
                float Height(Vector2 p) => stair.TrySampleTop(p, out float y) ? y : StreetHeight(p);
            }

            void AddHouse(CityHillsideBuilding house, string prefix, string pavement)
            {
                string id = prefix + "house:" + house.Lot.Cell.x + ":" + house.Lot.Cell.y;
                // A small loop in the entrance court survives two-core pruning while
                // residents can actually walk between their street and their front door.
                Rect court = house.CourtBounds;
                Vector2 center = court.center;
                float dx = Mathf.Min(.65f, court.width * .5f - AgentRadius - NavigationMargin);
                float dz = Mathf.Min(.65f, court.height * .5f - AgentRadius - NavigationMargin);
                if (dx <= 0f || dz <= 0f) return;
                int gate = Gate(id + ":gate", pavement, house.StreetContactWorld);
                int start = JoinPoint(id + ":street", gate, house.StreetContactWorld, Height);
                int a = CourtNode("walk", center + new Vector2(dx, -dz));
                int b = CourtNode("turn", center + new Vector2(-dx, -dz));
                // The long axis of a gable-facing entrance is offset from its porch centre.
                // Stay on the apron until reaching the court.
                Vector3 elbow = new Vector3(house.StreetContactWorld.x, house.DoorWorld.y, center.y);
                int approach = JoinPoint(id + ":apron", start, elbow, Height);
                // The usual front-facing approach already reached the exact entry centre.
                int entry = JoinPoint(id + ":entry", approach, GroundedPoint(center, Height), Height);
                Connect(id + ":court-a", entry, a, CityPedestrianLinkKind.Sidewalk, Height);
                Connect(id + ":court-b", a, b, CityPedestrianLinkKind.Sidewalk, Height);
                Connect(id + ":court-c", b, entry, CityPedestrianLinkKind.Sidewalk, Height);

                int CourtNode(string suffix, Vector2 p) => graph.AddNode(id + ":" + suffix, GroundedPoint(p, Height), false);
                float Height(Vector2 p) => house.TrySampleApproach(p, out float y) ? y : StreetHeight(p);
            }
        }

        private sealed partial class GraphBuilder
        {
            internal int SplitHillsideLane(string id, string prefix, Vector2 xz, Func<Vector2, float> height) =>
                SplitWalkingPathAt(id, CityPedestrianLinkKind.Sidewalk, prefix, xz, height, 1.2f * 1.2f);
        }
    }
}
