using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static partial class CityPedestrianPlanner
    {
        // Both end-to-end pavements and the flat crossing at the summit form
        // a loop in the city's existing graph. Residents reach it on foot.
        private static void BuildHillsidePaths(CityLayout layout, CityStreetSurfacePlan streets, GraphBuilder graph)
        {
            foreach (CityHillsideStreetRoute route in CityHillsideStreetPlan.ForLayout(layout).Routes)
            {
                string prefix = "hill:" + route.Bank + ":";
                int right = AddPavement(route.SidewalkPath, "right");
                int left = AddPavement(route.OppositeSidewalkPath, "left");
                Connect("summit-crossing", right, left, CityPedestrianLinkKind.Crosswalk, Height);

                foreach (CityHillsideShortcut stair in CityHillsideAccessPlan.ForLayout(layout).Shortcuts)
                {
                    if (stair.Bank != route.Bank) continue;
                    var points = stair.RouteWorld;
                    int previous = LaneGate("lower-stair", points[0]);
                    for (int i = 0; i < points.Count; i++)
                    {
                        int next = graph.AddNode(prefix + "stair:" + i, points[i], false);
                        Connect("stair:walk:" + i, previous, next, CityPedestrianLinkKind.Sidewalk,
                            point => stair.TrySampleTop(point, out float top) ? top : Height(point));
                        previous = next;
                    }
                    Connect("upper-stair-join", previous, LaneGate("upper-stair", points[points.Count - 1]),
                        CityPedestrianLinkKind.Sidewalk, Height);
                }

                int houseIndex = 0;
                foreach (CityHillsideBuilding house in CityHillsideNeighborhoodPlan.ForLayout(layout).Buildings)
                {
                    if (house.Bank != route.Bank) continue;
                    string key = "house:" + houseIndex++ + ":";
                    int gate = LaneGate(key + "gate", house.StreetContactWorld);
                    Rect court = house.CourtBounds;
                    // A short turnaround inside the court keeps the entrance
                    // usable by the two-core roaming graph as well as the hero.
                    Vector2 center = court.center;
                    float dx = Mathf.Min(.65f, court.width * .5f - AgentRadius - NavigationMargin);
                    float dz = Mathf.Min(.65f, court.height * .5f - AgentRadius - NavigationMargin);
                    if (dx <= 0f || dz <= 0f) continue;
                    int door = CourtNode("entry", center);
                    int a = CourtNode("walk", center + new Vector2(dx, -dz));
                    int b = CourtNode("turn", center + new Vector2(-dx, -dz));
                    int street = (XZ(graph.Nodes[gate].Position) - XZ(house.StreetContactWorld)).sqrMagnitude < .000001f ? gate :
                        graph.AddNode(prefix + key + "street", house.StreetContactWorld, false);
                    if (street != gate) Connect(key + "pavement", gate, street, CityPedestrianLinkKind.Sidewalk, HouseHeight);
                    Connect(key + "approach", street, door, CityPedestrianLinkKind.Sidewalk, HouseHeight);
                    Connect(key + "court-a", door, a, CityPedestrianLinkKind.Sidewalk, HouseHeight);
                    Connect(key + "court-b", a, b, CityPedestrianLinkKind.Sidewalk, HouseHeight);
                    Connect(key + "court-c", b, door, CityPedestrianLinkKind.Sidewalk, HouseHeight);

                    int CourtNode(string id, Vector2 pos) => graph.AddNode(prefix + key + id,
                        new Vector3(pos.x, HouseHeight(pos), pos.y), false);
                    float HouseHeight(Vector2 p) => house.TrySampleApproach(p, out float y) ? y : Height(p);
                }

                int AddPavement(IReadOnlyList<Vector3> vertices, string side)
                {
                    var shape = new List<Vector2>(vertices.Count);
                    foreach (Vector3 vertex in vertices) shape.Add(XZ(vertex));
                    var path = new CityRoadPath(shape);
                    Vector3 first = vertices[0], last = vertices[vertices.Count - 1];
                    int street = graph.SplitTerraceStreetAt(prefix + side + ":mouth", XZ(first), Height);
                    int start = (XZ(graph.Nodes[street].Position) - shape[0]).sqrMagnitude < .000001f ? street :
                        graph.AddNode(prefix + side + ":from", first, false);
                    int end = graph.AddNode(prefix + side + ":to", last, false);
                    Connect(side + ":join", street, start, CityPedestrianLinkKind.Sidewalk, Height);
                    graph.AddLink(prefix + "pavement:" + side, start, end, CityPedestrianLinkKind.Sidewalk, false, path, Height);
                    return end;
                }
                int LaneGate(string id, Vector3 point) => graph.SplitHillsideLane(prefix + id,
                    prefix + "pavement:", XZ(point), Height);
                float Height(Vector2 point)
                {
                    if (route.TrySampleTop(point, out float y, out _) ||
                        streets.SupportPlan.TrySampleTop(point, out y, out _)) return y;
                    throw new InvalidOperationException("Hillside pavement has no physical support at " + point);
                }
                void Connect(string id, int from, int to, CityPedestrianLinkKind kind, Func<Vector2, float> height)
                {
                    if (from == to) return;
                    Vector2 xz = XZ(graph.Nodes[from].Position), yz = XZ(graph.Nodes[to].Position);
                    if ((xz - yz).sqrMagnitude < .000001f)
                        throw new InvalidOperationException("Coincident hillside pedestrian gates at " + prefix + id);
                    graph.AddLink(prefix + id, from, to, kind, false, new CityRoadPath(new[] { xz, yz }), height);
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
