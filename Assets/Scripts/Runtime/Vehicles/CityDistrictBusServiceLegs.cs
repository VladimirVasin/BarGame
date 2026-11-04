using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static partial class CityBusPlanner
    {
        // First reserve usable platforms throughout this bank. Optimise over those stops,
        // since a nearby point on paper can require a long detour in the directed street graph.
        private static List<RouteOccurrence> ConnectDistrictStops(CityLayout layout, DistrictGraph graph,
            IReadOnlyList<TemporaryNode> nodes, List<StopCandidate> stops, List<Vector3> entrances,
            Vector2 low, Vector2 high, List<StopTarget> targets)
        {
            var links = graph.Links;
            var available = new List<StopCandidate>();
            float inset = CityBusDesignVehicle.Default.InflatedLength * .5f + StopJunctionClearance + CandidateEndInset;
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                if (!link.IsRoadSegment || !graph.Contains(link) || layout.IsRiverBridgeEdge(link.RoadEdge) ||
                    link.Length <= inset * 2f) continue;
                float d = link.Length * .5f;
                if (!TryDistrictPlatform(layout, link.Samples, entrances, inset, link.Length-inset, ref d, out var pole)) continue;
                EvaluateSamples(link.Samples, d, out var p, out var f);
                var n = nodes[link.FromNodeIndex];
                var cell = GetRightSideCell(n.FromGridNode, n.ToGridNode);
                available.Add(new StopCandidate(null, link, i, cell, d, p, f, pole, -Vector3.Cross(Vector3.up, f).normalized,
                    0, 0, 0f, !IsLevelStopEdge(layout, link.RoadEdge)));
            }

            var suffixes = new Dictionary<string,int>();
            var localTargets = targets.FindAll(t => GetTargetBank(t,layout.River.Definition.CorridorCellX) ==
                (graph.East ? RiverBank.East : RiverBank.West));
            var walks = new Dictionary<(StopTarget, TemporaryLink, Vector3), float>();
            var missing = new List<(StopTarget place,float walk)>();
            foreach (var place in localTargets)
            {
                if (stops.Exists(s => s.Target.Id == place.Id)) continue;
                float existing = float.PositiveInfinity;
                foreach (var s in stops) existing = Mathf.Min(existing, WalkTo(place, s));
                missing.Add((place, existing));
            }
            missing.Sort((a,b) => DistrictStopRank(a.place.Kind) != DistrictStopRank(b.place.Kind) ?
                DistrictStopRank(a.place.Kind).CompareTo(DistrictStopRank(b.place.Kind)) : b.walk.CompareTo(a.walk));
            foreach (var target in missing)
            {
                if (stops.Count >= 8) break;
                if (target.walk <= 100f || stops.Exists(s => WalkTo(target.place, s) <= 100f)) continue;
                StopCandidate best = null; float bestWalk = Mathf.Min(MaximumStopWalkDistance, target.walk-30f);
                foreach (var c in available)
                {
                    if (!Distinct(c) || XzDistance(c.ShelterPosition, target.place.ReferencePositions[0]) > 180f) continue;
                    float walk = WalkTo(target.place, c);
                    if (walk > bestWalk || (best != null && walk >= bestWalk)) continue;
                    best = c; bestWalk = walk;
                }
                if (best != null) stops.Add(Accept(best, target.place, bestWalk));
            }

            // A bank may have very few usable frontage kerbs. In an uncovered neighbourhood
            // serve its most remote accessible street instead of choosing another pole at an existing corner.
            while (stops.Count < 7)
            {
                StopCandidate extra = null;
                float best = stops.Count < 5 ? 0f : 115f;
                foreach (var c in available)
                {
                    if (!Distinct(c)) continue;
                    float nearest = float.PositiveInfinity;
                    foreach (var stop in stops) nearest = Mathf.Min(nearest, DistrictWalkDistance(layout,
                        c.Source.RoadEdge,c.ShelterPosition,stop.Source.RoadEdge,stop.ShelterPosition));
                    if (nearest <= best) continue;
                    best = nearest; extra = c;
                }
                if (extra == null) break;
                string suffix = CreateSpacingSuffix(extra.Position, low, high, suffixes);
                var label = new StopTarget(CityBusStopTargetKind.LoopSpacing, suffix, extra.RoadsideCell,
                    graph.East ? CityDistrictKind.Residential : CityDistrictKind.Industrial,
                    new List<RoadEdge> { extra.Source.RoadEdge }, new List<Vector3> { extra.ShelterPosition },
                    new List<Rect>(), suffix, false);
                stops.Add(Accept(extra, label, 0f));
            }

            // Eligible alternatives on another right-hand kerb can save a detour without moving
            // the interchange or the home. Use the nearest safe alternative at that same named destination.
            var options = new List<List<StopCandidate>>();
            foreach (var stop in stops)
            {
                var choices = new List<StopCandidate> { stop };
                if (stop.Target.Kind != CityBusStopTargetKind.Transfer && stop.Target.Kind != CityBusStopTargetKind.LoopSpacing)
                {
                    foreach (var c in available)
                    {
                        if (c.Source == stop.Source || XzDistance(c.ShelterPosition, stop.ShelterPosition) > 42f ||
                            stops.Exists(s => s != stop && XzDistance(s.ShelterPosition,c.ShelterPosition) < MinimumPlanarStopSpacing)) continue;
                        float walk = WalkTo(stop.Target,c);
                        if (walk <= Mathf.Min(MaximumStopWalkDistance, stop.ReferenceDistance + 35f))
                            choices.Add(Accept(c,stop.Target,walk));
                    }
                }
                options.Add(choices);
            }

            var distances = new Dictionary<int,float[]>();
            float[] Distances(StopCandidate s)
            {
                int key = s.Source.ToNodeIndex;
                if (!distances.TryGetValue(key,out var result))
                    distances.Add(key,result = DistrictDistances(key, graph.Outgoing, links, nodes, null, false));
                return result;
            }

            // A directed travelling-salesman circuit over a bounded roster. Optimise both total driving
            // and long gaps; explicitly cost the final return to the first stop as well.
            float current = Tour(stops, out var ordered);
            for (int pass = 0; pass < 2; pass++)
            {
                bool improved = false;
                for (int i = 0; i < stops.Count; i++)
                {
                    var kept = stops[i];
                    foreach (var alternative in options[i])
                    {
                        if (alternative == kept) continue;
                        stops[i] = alternative;
                        float cost = Tour(stops,out var permutation);
                        if (cost >= current - 1f) continue;
                        current = cost; kept = alternative; ordered = permutation; improved = true;
                    }
                    stops[i] = kept;
                }
                if (!improved) break;
            }
            var chosen = new List<StopCandidate>(stops);
            stops.Clear();
            foreach (int i in ordered) stops.Add(chosen[i]);
            var route = new List<RouteOccurrence>();
            var blocked = CreateStopStreetSet(nodes,stops);
            for (int i = 0; i < stops.Count; i++)
            {
                var a = stops[i]; var b = stops[(i+1)%stops.Count];
                route.Add(new RouteOccurrence(a.Source,a));
                // Prevent circling the district to avoid a different pole. The whole lap serves each
                // scheduled station once; grade, handedness and collision are already proven per connector.
                if (!TryFindPath(a.Source.ToNodeIndex, b.Source.FromNodeIndex, null, null, nodes,
                    graph.Outgoing, links, out var shortest))
                    throw new CityDistrictServiceUnavailable("Disconnected district service stops.");
                if (TryFindPath(a.Source.ToNodeIndex,b.Source.FromNodeIndex,null,blocked,nodes,graph.Outgoing,links,
                        out var avoiding) && Length(avoiding) <= Length(shortest)+12f) shortest = avoiding;
                foreach (int edge in shortest) route.Add(new RouteOccurrence(links[edge],null));
            }
            return route;

            float Length(List<int> path) { float sum=0; foreach(int k in path) sum+=links[k].Length; return sum; }
            bool Distinct(StopCandidate c) => !stops.Exists(s => s.Source.RoadEdge == c.Source.RoadEdge ||
                XzDistance(s.ShelterPosition,c.ShelterPosition) < MinimumPlanarStopSpacing);
            StopCandidate Accept(StopCandidate c, StopTarget target,float walk) =>
                new StopCandidate(target,c.Source,c.SourceLinkIndex,c.RoadsideCell,c.DistanceAlongLink,c.Position,
                    c.Forward,c.ShelterPosition,c.RoadsideForward,0,0,walk,c.IsGraded);

            float WalkTo(StopTarget t, StopCandidate c)
            {
                var key = (t,c.Source,c.ShelterPosition);
                if (walks.TryGetValue(key,out float cached)) return cached;
                float shortest=float.PositiveInfinity;
                foreach (var reference in t.ReferencePositions)
                foreach (var edge in t.FrontageEdges)
                {
                    if (!layout.HasRoad(edge) || XzDistance(c.ShelterPosition,reference) >= shortest) continue;
                    var path=layout.RoadGeometry.Get(edge);
                    if (path.Project(new Vector2(reference.x,reference.z)).DistanceSquared > 900f) continue;
                    shortest=Mathf.Min(shortest,DistrictWalkDistance(layout,c.Source.RoadEdge,c.ShelterPosition,edge,reference));
                }
                walks[key] = shortest;
                return shortest;
            }

            float Tour(List<StopCandidate> points,out int[] order)
            {
                int n=points.Count;
                // Transport has a bounded roster. A larger legacy/custom layout still uses its existing tour.
                if (n>10)
                {
                    order=new int[n]; for(int i=0;i<n;i++) order[i]=i;
                    float sum=0; for(int i=0;i<n;i++) sum+=Cost(i,(i+1)%n);
                    return sum;
                }
                int size=1<<n;
                var cost=new float[size*n]; var previous=new int[size*n];
                for(int i=0;i<cost.Length;i++) cost[i]=float.PositiveInfinity;
                cost[n]=0f;
                for(int mask=1;mask<size;mask+=2)
                    for(int last=0;last<n;last++)
                    {
                        int state=mask*n+last;
                        if(float.IsPositiveInfinity(cost[state])) continue;
                        for(int next=1;next<n;next++)
                        {
                            int bit=1<<next;
                            if((mask&bit)!=0) continue;
                            int target=(mask|bit)*n+next;
                            float value=cost[state]+Cost(last,next);
                            if(value<cost[target]) {cost[target]=value;previous[target]=last;}
                        }
                    }
                int full=size-1; float best=float.PositiveInfinity; int cursor=0;
                for(int i=1;i<n;i++)
                {
                    float value=cost[full*n+i]+Cost(i,0);
                    if(value<best) {best=value;cursor=i;}
                }
                order=new int[n];
                for(int i=n-1;i>0;i--)
                {
                    order[i]=cursor;
                    int prior=previous[full*n+cursor];
                    full^=1<<cursor; cursor=prior;
                }
                return best;

                float Cost(int a,int b)
                {
                    var first=points[a]; var second=points[b];
                    float metres=first.Source.Length-first.DistanceAlongLink+Distances(first)[second.Source.FromNodeIndex]+second.DistanceAlongLink;
                    return metres + 1.75f*Mathf.Max(0f,metres-310f) + 4f*Mathf.Max(0f,DistrictStopMinimum-metres);
                }
            }
        }

        private static List<CityBusStopDescriptor> CompleteDistrictMacroStops(CityLayout layout, bool east,
            List<CityBusRouteNode> nodes, List<CityBusRouteLink> links, float length, List<CityBusStopDescriptor> stops,
            Vector2 low, Vector2 high)
        {
            var starts = new float[links.Count];
            for (int i = 1; i < links.Count; i++) starts[i] = starts[i-1] + links[i-1].Length;
            var suffixes = SeedSpacingSuffixCounts(stops);
            var added = new List<CityBusStopDescriptor>();
            for (int i = 0; i < stops.Count; i++)
            {
                float start = stops[i].DistanceAlongLoop;
                float end = stops[(i+1)%stops.Count].DistanceAlongLoop + (i == stops.Count-1 ? length : 0f);
                if (end - start < 310f) continue;
                float middle = (start + end) * .5f;
                int best = -1;
                float bestDistance = 0f, offset = float.PositiveInfinity;
                Vector3 bestPole = default;
                RoadEdge bestEdge = default;
                for (int index = 0; index < links.Count; index++)
                {
                    var link = links[index];
                    if (link.Kind == CityBusRouteLinkKind.Straight) continue;
                    float s = starts[index]; if (s < start) s += length;
                    if (s >= end-DistrictStopMinimum || s+link.Length <= start+DistrictStopMinimum) continue;
                    if (!TryMacroPlatform(layout, link, nodes[link.FromNodeIndex], nodes[link.ToNodeIndex], middle-s,
                            out float along, out var pole, out var edge)) continue;
                    float station = s+along;
                    float delta = Mathf.Abs(station-middle);
                    if (delta >= offset || station-start < DistrictStopMinimum || end-station < DistrictStopMinimum ||
                        !IsPlanarClearOf(pole, stops) || !IsPlanarClearOf(pole, added)) continue;
                    best = index; bestDistance = along; bestPole = pole; bestEdge = edge; offset = delta;
                }
                if (best < 0) continue;
                EvaluateSamples(links[best].Samples, bestDistance, out var pos, out var facing);
                string suffix = CreateSpacingSuffix(pos, low, high, suffixes);
                var district = east ? CityDistrictKind.Residential : CityDistrictKind.Industrial;
                string id = "bus-stop:" + layout.BlueprintId + ":route-01:" + suffix;
                added.Add(new CityBusStopDescriptor(id, "", "bus.stop.default_coastal." + suffix.Replace('-', '_'), 0,
                    starts[best] + bestDistance, district, bestPole, -Vector3.Cross(Vector3.up,facing).normalized,
                    best, bestDistance, pos, facing, bestEdge, CityBusStopTargetKind.LoopSpacing, suffix, bestEdge.A));
            }
            if (added.Count == 0) return stops;
            stops.AddRange(added);
            stops.Sort((a,b) => a.DistanceAlongLoop.CompareTo(b.DistanceAlongLoop));
            return RenumberStops(stops);
        }

        private static float[] DistrictDistances(int start, IReadOnlyList<List<int>> adjoining, List<TemporaryLink> links,
            IReadOnlyList<TemporaryNode> nodes, HashSet<DirectedKey> blocked, bool reverse)
        {
            var dist = new float[adjoining.Count];
            for (int i = 0; i < dist.Length; i++) dist[i] = float.PositiveInfinity;
            dist[start] = 0f;
            var heap = new List<PathHeapEntry> { new PathHeapEntry(start, 0f) };
            while (heap.Count != 0)
            {
                var item = PopPathEntry(heap);
                if (item.Distance > dist[item.NodeIndex]) continue;
                foreach (int i in adjoining[item.NodeIndex])
                {
                    var link = links[i];
                    var a = nodes[link.FromNodeIndex]; var b = nodes[link.ToNodeIndex];
                    if (blocked != null && (link.OccupiesPrimaryRoadEdge && blocked.Contains(new DirectedKey(a.FromGridNode,a.ToGridNode)) ||
                        link.HasSecondaryRoadEdge && blocked.Contains(new DirectedKey(b.FromGridNode,b.ToGridNode)))) continue;
                    int next = reverse ? link.FromNodeIndex : link.ToNodeIndex;
                    float d = item.Distance + link.Length;
                    if (d >= dist[next]) continue;
                    dist[next] = d;
                    heap.Add(new PathHeapEntry(next, d));
                    BubbleUpPathEntry(heap);
                }
            }
            return dist;
        }
    }
}
