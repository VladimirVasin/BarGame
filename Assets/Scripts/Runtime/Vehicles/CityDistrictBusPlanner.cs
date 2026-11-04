using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    internal sealed class CityDistrictServiceUnavailable : InvalidOperationException
    {
        public CityDistrictServiceUnavailable(string reason) : base(reason) { }
    }

    /// <summary>City service in passenger-facing order. Geometry is shared by map, traffic and actors.</summary>
    public static class CityDistrictBusPlanner
    {
        private static readonly ConditionalWeakTable<CityLayout, IReadOnlyList<CityBusPlan>> Routing = new();
        private static readonly ConditionalWeakTable<CityLayout, IReadOnlyList<CityBusPlan>> Grounded = new();

        public static IReadOnlyList<CityBusPlan> CreateRoadRouting(CityLayout layout) =>
            Routing.GetValue(layout, RouteLayout);

        private static IReadOnlyList<CityBusPlan> RouteLayout(CityLayout layout)
        {
            try { return CityBusPlanner.BuildDistrictRouting(layout); }
            catch (CityDistrictServiceUnavailable) when (layout.Seed != GameSessionState.DefaultCitySeed)
            {
                // An alternate generated layout can lack a usable approach at either bank.
                return Array.AsReadOnly(new[] { CityBusPlanner.CreateRoadRouting(layout) });
            }
        }

        public static IReadOnlyList<CityBusPlan> Create(CityLayout layout) =>
            Grounded.GetValue(layout, Ground);

        private static IReadOnlyList<CityBusPlan> Ground(CityLayout layout)
        {
            var routes = CreateRoadRouting(layout);
            var surface = CityStreetSurfacePlanner.Create(layout);
            var result = new CityBusPlan[routes.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = CityBusPlanner.GroundDistrictRoute(layout, surface, routes[i]);
            return Array.AsReadOnly(result);
        }
    }

    public static partial class CityBusPlanner
    {
        internal static CityBusPlan GroundDistrictRoute(CityLayout layout, CityStreetSurfacePlan surface, CityBusPlan route)
        {
            var stops = GroundShelterPositions(layout, surface, route.Stops, out _);
            if (route.Number == 3)
            {
                // The grid terrain sampler has no hill-side verge. These stops
                // already carry the actual cut-and-fill surface of their terrace.
                for (int i = 0; i < stops.Count; i++)
                    if (route.Stops[i].RoadEdge.A == route.Stops[i].RoadEdge.B) stops[i] = route.Stops[i];
            }
            return new CityBusPlan(route, route.DecorationSeed, stops);
        }

        internal static IReadOnlyList<CityBusPlan> BuildDistrictRouting(CityLayout layout)
        {
            if (!layout.SpatialPlan.IsCompactCoastal || CityHillsideStreetPlan.ForLayout(layout).Routes.Count != 2)
                return Array.AsReadOnly(new[] { CreateRoadRouting(layout) });

            var vehicle = CityBusDesignVehicle.Default;
            float lane = (layout.RoadWidth - 2f * CityStreetSurfacePlanner.SidewalkWidth) * .25f;
            BuildRoutingGraph(layout, vehicle, lane, System.Diagnostics.Stopwatch.StartNew(),
                out var nodes, out var all, out _, out _);
            var targets = CreateStopTargets(layout);
            targets.Sort((a, b) => DistrictStopRank(a.Kind) != DistrictStopRank(b.Kind)
                ? DistrictStopRank(a.Kind).CompareTo(DistrictStopRank(b.Kind))
                : string.CompareOrdinal(a.Id, b.Id));
            var entrances = CreateEntranceClearancePoints(layout);

            // The two southern city rows are the strip shared with the hill district.
            // The only crossing available to this connector is the Works road bridge.
            var strip = SelectLinks(edge => edge.A.y <= 1 && edge.B.y <= 1);
            var adjacency = CreateRouteAdjacency(nodes.Count, strip, false);
            int first = DirectedRoad(strip, 12, 0, 11, 0);
            int last = DirectedRoad(strip, 2, 0, 1, 0);
            // The hill mouths own their approaches. A wide turn may otherwise replace the entire first
            // or last grid edge with an S-merge, leaving no stable tangent on which to splice the hill.
            if (first < 0 || last < 0 || !TryFindPath(strip[first].ToNodeIndex, strip[last].FromNodeIndex,
                    null, null, nodes, adjacency, strip, out var chain))
            {
                // Generated survey seeds may omit the short cross streets used by the production map.
                if (layout.Seed != GameSessionState.DefaultCitySeed)
                    return Array.AsReadOnly(new[] { CreateRoadRouting(layout) });
                throw new CityDistrictServiceUnavailable("No bus-safe Works bridge circuit between the two hill mouths.");
            }
            chain.Insert(0, first);
            chain.Add(last);
            var returnLinks = new List<CityBusRouteLink>();
            var returnNodes = new List<CityBusRouteNode>();
            for (int i = 0; i < chain.Count; i++)
            {
                TemporaryLink link = strip[chain[i]];
                returnLinks.Add(CopyLink(link, i, i + 1, "route-03"));
                returnNodes.Add(CopyNode(nodes[link.FromNodeIndex], i, "route-03"));
            }
            returnNodes.Add(CopyNode(nodes[strip[chain[chain.Count - 1]].ToNodeIndex], chain.Count, "route-03"));
            AddDistrictQuayTurns(layout, nodes, all);
            var west = BankGraph(false);
            var east = BankGraph(true);
            StopCandidate westPlatform = null, eastPlatform = null;
            var bridgeChoices = new[] { 11, 6, 1 }; // mouth, park footbridge, works bridge.
            string crossingKey = "mouth";
            var traces = new List<string>();
            foreach (int row in bridgeChoices)
            {
                var crossing = new RoadEdge(new Vector2Int(6, row), new Vector2Int(7, row));
                var westMouth = TransferSites(west, "mouth", crossing.A, null);
                var eastMouth = TransferSites(east, "mouth", crossing.B, null, traces);
                Vector3 middle = (layout.GetNodeWorldPosition(crossing.A) + layout.GetNodeWorldPosition(crossing.B)) * .5f;
                var eastApproach = new float[eastMouth.Count];
                for (int i = 0; i < eastMouth.Count; i++) eastApproach[i] = WalkToBridge(eastMouth[i]);
                float bestWalk = float.PositiveInfinity;
                StopCandidate firstDock = null, secondDock = null;
                foreach (var w in westMouth)
                {
                    float westApproach = WalkToBridge(w);
                    for (int i = 0; i < eastMouth.Count; i++)
                    {
                        var e = eastMouth[i];
                        float walk = westApproach + eastApproach[i];
                        if (walk >= bestWalk) continue;
                        bestWalk = walk;
                        firstDock = w; secondDock = e;
                    }
                }
                if (firstDock == null) { traces.Add($"row {row}: no accessible stops"); continue; }
                float separation = XzDistance(firstDock.ShelterPosition, secondDock.ShelterPosition);
                traces.Add($"row {row}: {firstDock.Source.RoadEdge}/{firstDock.ShelterPosition} - " +
                    $"{secondDock.Source.RoadEdge}/{secondDock.ShelterPosition}, separation {separation:F0}m, walk {bestWalk:F0}m");
                if (separation >= 85f || bestWalk > 115f) continue;
                westPlatform = firstDock; eastPlatform = secondDock;
                crossingKey = row == 11 ? "mouth" : row == 6 ? "park" : "works";
                if (row != 11) Debug.Log("CITY BUS TRANSFER: " + string.Join("; ", traces));
                break;

                float WalkToBridge(StopCandidate c) => DistrictWalkDistance(layout, c.Source.RoadEdge, c.ShelterPosition, crossing, middle);
            }
            if (westPlatform == null)
                throw new CityDistrictServiceUnavailable("No short walking interchange on the two banks: " + string.Join("; ", traces));
            var hill = CityHillsideBusPlanner.Create(layout, returnLinks, returnNodes);
            return Array.AsReadOnly(new[] { BankCircuit(west, westPlatform), BankCircuit(east, eastPlatform), hill });

            int DirectedRoad(List<TemporaryLink> links, int x0, int z0, int x1, int z1)
            {
                var from = new Vector2Int(x0, z0);
                var to = new Vector2Int(x1, z1);
                return links.FindIndex(link => link.IsRoadSegment &&
                    nodes[link.FromNodeIndex].FromGridNode == from && nodes[link.FromNodeIndex].ToGridNode == to);
            }

            List<TemporaryLink> SelectLinks(Predicate<RoadEdge> allows)
            {
                return all.FindAll(link => allows(nodes[link.FromNodeIndex].RoadEdge) &&
                                           allows(nodes[link.ToNodeIndex].RoadEdge));
            }

            DistrictGraph BankGraph(bool eastBank)
            {
                int bank = layout.River.Definition.CorridorCellX;
                var links = SelectLinks(edge => eastBank ? edge.A.x > bank && edge.B.x > bank :
                    edge.A.x <= bank && edge.B.x <= bank);
                var outgoing = CreateRouteAdjacency(nodes.Count, links, false);
                int[] components = LabelStronglyConnectedComponents(outgoing, links);
                // The selected component must include a complete directed cycle. Dead ends, even close
                // to a stop in plan view, can neither bring a passenger back nor serve the next dock.
                var counts = new Dictionary<int, int>();
                int main = -1, max = 0;
                foreach (var link in links)
                {
                    int c = components[link.FromNodeIndex];
                    if (!link.IsRoadSegment || c != components[link.ToNodeIndex]) continue;
                    counts.TryGetValue(c, out int count);
                    counts[c] = ++count;
                    if (count > max) { main = c; max = count; }
                }

                return new DistrictGraph { East = eastBank, Links = links, Outgoing = outgoing,
                    Components = components, Main = main };
            }

            CityBusPlan BankCircuit(DistrictGraph graph, StopCandidate mouth)
            {
                bool eastBank = graph.East;
                int bank = layout.River.Definition.CorridorCellX;
                var links = graph.Links;
                int mi = eastBank ? 7 : 0, ma = eastBank ? 13 : 6;
                Vector3 corner0 = layout.GetNodeWorldPosition(new Vector2Int(mi, 0));
                Vector3 corner1 = layout.GetNodeWorldPosition(new Vector2Int(ma, 12));
                var low = new Vector2(corner0.x, corner0.z);
                var high = new Vector2(corner1.x, corner1.z);
                var selected = new List<StopCandidate>();
                string slope = eastBank ? "east-slope" : "west-slope";
                CityBusStopDescriptor shared = null;
                foreach (var dock in hill.Stops)
                    if (dock.TargetId == "transfer:" + slope) { shared = dock; break; }
                var sites = TransferSites(graph, slope, new Vector2Int(eastBank ? 12 : 1, 0), shared);
                if (sites.Count == 0) throw new CityDistrictServiceUnavailable("No hillside transfer platform for " + slope);
                selected.Add(sites[0]);
                selected.Add(mouth);
                // Preserve existing named destinations. Closely spaced frontages share a stop
                // according to the same walking/spacing budget as the single-route planner.
                foreach (var target in targets)
                {
                    if (GetTargetBank(target, bank) != (eastBank ? RiverBank.East : RiverBank.West)) continue;
                    var candidates = CreateStopCandidates(layout, vehicle, target, nodes, links, entrances, true);
                    var c = candidates.Find(candidate => graph.Contains(candidate.Source));
                    if (c == null || selected.Exists(s => s.Source.RoadEdge == c.Source.RoadEdge ||
                        target.Kind != CityBusStopTargetKind.PlayerHome &&
                        XzDistance(s.ShelterPosition, c.ShelterPosition) < MinimumStopSpacing &&
                        DistrictWalkDistance(layout, s.Source.RoadEdge, s.ShelterPosition, c.Source.RoadEdge,
                            c.ShelterPosition) + c.ReferenceDistance <= MaximumStopWalkDistance)) continue;
                    selected.Add(c);
                }

                selected.Sort((a, b) =>
                {
                    int comparison = GetPerimeterStation(low, high, a.Position)
                        .CompareTo(GetPerimeterStation(low, high, b.Position));
                    return comparison != 0 ? comparison : string.CompareOrdinal(a.Target.Id, b.Target.Id);
                });

                var occurrences = ConnectDistrictStops(layout, graph, nodes, selected, entrances, low, high, targets);
                string suffix = eastBank ? "route-02" : "route-01";
                string routeId = "bus-route:" + layout.BlueprintId + ":" + suffix;
                var roadNodes = new List<CityBusRouteNode>();
                var roadLinks = new List<CityBusRouteLink>();
                var metadata = new List<RouteLinkMetadata>();
                var ordered = new List<int>();
                float length = 0f;
                for (int i = 0; i < occurrences.Count; i++)
                {
                    var source = occurrences[i].Source;
                    roadNodes.Add(CopyNode(nodes[source.FromNodeIndex], i, suffix));
                    var link = CopyLink(source, i, (i + 1) % occurrences.Count, suffix);
                    roadLinks.Add(link);
                    metadata.Add(new RouteLinkMetadata(source, i, occurrences[i].StopCandidate));
                    ordered.Add(i);
                    length += link.Length;
                }
                var stops = CreateTargetStops(layout, metadata, roadLinks);
                stops = InsertSpacingStops(layout, vehicle, nodes, metadata, roadLinks, length, stops, true);
                stops = CompleteDistrictMacroStops(layout, graph.East, roadNodes, roadLinks, length, stops, low, high);
                bool shopHasOwnStop = stops.Exists(stop => stop.TargetKind == CityBusStopTargetKind.Supermarket);
                for (int i = 0; i < stops.Count; i++)
                {
                    var s = stops[i];
                    bool transfer = s.TargetId.StartsWith("transfer:", StringComparison.Ordinal);
                    string id = transfer ? routeId + ":" + s.TargetId : s.Id.Replace(":route-01:", ":" + suffix + ":");
                    string key = s.TargetId == "transfer:mouth" ? "map.bus.transfer." + crossingKey :
                        transfer ? "map.bus.transfer." + s.TargetId.Substring(9).Replace('-', '_') : s.NameLocalizationKey;
                    if (s.TargetKind == CityBusStopTargetKind.PlayerHome && !shopHasOwnStop && layout.Supermarket != null &&
                        layout.TryGetFrontageEdge(layout.Supermarket, out var shopStreet) && DistrictWalkDistance(layout,
                            s.RoadEdge, s.ShelterPosition, shopStreet, layout.Supermarket.SidewalkArrivalPosition) <= 85f)
                        key = "bus.stop.default_coastal.home_supermarket";
                    stops[i] = new CityBusStopDescriptor(id, s.SourceDecorationId, key, i, s.DistanceAlongLoop, s.District,
                        s.ShelterPosition, s.RoadsideForward, s.LinkIndex, s.DistanceAlongLink, s.Position,
                        s.Forward, s.RoadEdge, s.TargetKind, s.TargetId, s.TargetCell);
                }
                var anchors = CreateSpawnAnchors(layout, vehicle, metadata, roadLinks, stops);
                return new CityBusPlan(layout.Seed, layout.Seed, CityPedestrianStableHash.Combine((uint)layout.Seed,
                    CityPedestrianStableHash.String(routeId)), routeId, ordered, length, lane, vehicle,
                    roadNodes, roadLinks, anchors, stops, Array.Empty<CityBusClearanceFailure>(), nodes.Count, links.Count);

            }

            List<StopCandidate> TransferSites(DistrictGraph graph, string name, Vector2Int node, CityBusStopDescriptor shared,
                List<string> diagnostics = null)
            {
                Vector3 reference = shared == null ? layout.GetNodeWorldPosition(node) : shared.ShelterPosition;
                Rect partner = shared == null ? default : CityBusStopWorldBuilder.DescribeFurnitureFootprint(shared);
                var district = graph.East ? CityDistrictKind.Residential : CityDistrictKind.Industrial;
                var target = new StopTarget(CityBusStopTargetKind.Transfer, "transfer:" + name, node, district,
                    new List<RoadEdge>(), new List<Vector3> { reference }, new List<Rect>(), name, true);
                var sites = new List<StopCandidate>();
                var inside = new HashSet<RoadEdge>();
                var legal = new HashSet<RoadEdge>();
                for (int i = 0; i < graph.Links.Count; i++)
                {
                    var link = graph.Links[i];
                    float inset = vehicle.InflatedLength * .5f + StopJunctionClearance;
                    bool forbidden = name == "mouth" ? layout.IsRiverBridgeEdge(link.RoadEdge) :
                        layout.IsBusFurnitureExcluded(link.RoadEdge);
                    if (!link.IsRoadSegment || forbidden ||
                        link.Length <= (inset + CandidateEndInset) * 2f ||
                        XzDistance(link.Samples[0].Position, reference) > MaximumStopWalkDistance + link.Length) continue;
                    bool nearQuay = diagnostics != null && link.RoadEdge.A.x >= 7 && link.RoadEdge.B.x <= 9 &&
                        Mathf.Abs(link.RoadEdge.A.y - node.y) <= 2 && Mathf.Abs(link.RoadEdge.B.y - node.y) <= 2;
                    bool inCircuit = graph.Contains(link);
                    if (!inCircuit && !nearQuay) continue;
                    if (inCircuit && nearQuay) inside.Add(link.RoadEdge);
                    var n = nodes[link.FromNodeIndex];
                    var cell = GetRightSideCell(n.FromGridNode, n.ToGridNode);
                    if (!TryCreateStopCandidate(layout, target, link, i, cell, 0, 0, inset, entrances,
                            out var c, true)) continue;
                    if (nearQuay) legal.Add(link.RoadEdge);
                    if (!inCircuit) continue;
                    if (shared != null && c.ReferenceDistance > .05f && partner.Overlaps(
                        CityBusStopWorldBuilder.DescribeFurnitureFootprint(c.ShelterPosition,
                            c.Forward, c.RoadsideForward))) continue;
                    sites.Add(c);
                }
                if (diagnostics != null)
                    diagnostics.Add($"east row {node.y} approaches in cycle ({string.Join(",", inside)}); clear sites ({string.Join(",", legal)})");
                sites.Sort((a, b) => a.ReferenceDistance != b.ReferenceDistance ? a.ReferenceDistance.CompareTo(b.ReferenceDistance)
                    : a.SourceLinkIndex.CompareTo(b.SourceLinkIndex));
                // The far side always has to cross the same bridge; more distant sites cannot improve this walk.
                if (sites.Count > 12) sites.RemoveRange(12, sites.Count - 12);
                return sites;
            }
        }

        private sealed class DistrictGraph
        {
            public bool East;
            public List<TemporaryLink> Links;
            public List<int>[] Outgoing;
            public int[] Components;
            public int Main;
            public bool Contains(TemporaryLink link) => Components[link.FromNodeIndex] == Main &&
                Components[link.ToNodeIndex] == Main;
        }

        private static CityBusRouteLink CopyLink(TemporaryLink link, int index, int next, string suffix) =>
            new CityBusRouteLink(link.Id + ":" + suffix + ":" + index, index, next,
                link.Kind, link.JunctionNode, link.Samples, link.MinimumTurnRadius, link.Clearance);

        private static CityBusRouteNode CopyNode(TemporaryNode node, int index, string suffix) =>
            new CityBusRouteNode(node.Id + ":" + suffix + ":" + index, node.Position, node.Forward, node.RoadEdge,
                node.FromGridNode, node.ToGridNode, new[] { index });
    }
}
