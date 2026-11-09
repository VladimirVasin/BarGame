using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCoastalInfrastructurePlan
    {
        private const int MaximumCorePoles = 28;
        internal const float MouthFeedRadius = 52f;

        private static void GrowCore(CityLayout layout, CityFringeYardPlan fringe, IList<CityCoastalFacility> facilities,
            ClearanceContext clearance, List<Vector3> candidates, List<Vector3> poles, List<Vector3> forwards,
            List<Rect> solids, List<Vector2Int> connections)
        {
            // Give each surveyed street a station before offering its alternative stations.
            // Street additions cannot push an entire distant street out by changing its index.
            var streets = new List<(CityRoadPath path, List<Vector3> stations)>();
            foreach (var edge in layout.RoadEdges)
            {
                var stations = new List<Vector3>(12);
                AddRoadCandidates(layout, fringe, facilities, stations, clearance, edge, 12);
                if (stations.Count != 0) streets.Add((layout.RoadGeometry.Get(edge), stations));
            }
            var bearings = new List<Vector3>(MaximumPoleCandidateCount);
            for (int rank = 0; rank < 12 && candidates.Count < MaximumPoleCandidateCount; rank++)
            foreach (var street in streets)
            {
                if (rank >= street.stations.Count || candidates.Count >= MaximumPoleCandidateCount) continue;
                Vector3 position = street.stations[rank];
                bool duplicate = false;
                foreach (var other in candidates)
                    if ((other - position).sqrMagnitude < 16f) { duplicate = true; break; }
                if (duplicate) continue;
                var along = street.path.Project(new Vector2(position.x, position.z)).DistanceAlong;
                var tangent = street.path.SampleDistance(along).Tangent;
                candidates.Add(position);
                bearings.Add(new Vector3(tangent.x, 0f, tangent.y));
            }
            // Bearings follow the street, independent of the direction from which the
            // search first reaches a pole. Thus clearance is a property of one undirected edge.
            var route = new CoreFeedRoutes(layout, facilities, clearance, candidates, bearings,
                poles, forwards, solids, connections);
            foreach (var street in CityHillsideStreetPlan.ForLayout(layout).Routes)
            {
                Vector3 mouth = layout.GetNodeWorldPosition(street.MouthNode);
                if (!route.Connect(mouth, MouthFeedRadius))
                    throw new InvalidOperationException($"No coastal feeder reaches the {street.Bank} Ridge mouth at {mouth} " +
                        $"within {MouthFeedRadius} m ({poles.Count} installed poles, {candidates.Count} surveyed sites).");
            }
            foreach (var lot in layout.BuildingLots)
                if (lot.IsBar) route.Connect(lot.SidewalkArrivalPosition, 24f);
            if (layout.Supermarket != null) route.Connect(layout.Supermarket.SidewalkArrivalPosition, 24f);
            var port = CityPortAccessPlan.ForLayout(layout);
            if (port != null) route.Connect(port.StreetConnection, 32f);

            // The four outlying inhabited districts are defined by ordinary lower-city
            // addresses, so ocean, mountains, park corners and the new summit never win.
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                Vector3 direction = new Vector3((quadrant & 1) == 0 ? -1 : 1, 0, (quadrant & 2) == 0 ? 1 : -1);
                BuildingLot terminal = null;
                float farthest = float.NegativeInfinity;
                foreach (var lot in layout.BuildingLots)
                {
                    if (!lot.IsOrdinaryBuilding || !lot.HasRoadFrontage) continue;
                    float distance = Vector3.Dot(lot.SidewalkArrivalPosition, direction);
                    if (distance > farthest) { farthest = distance; terminal = lot; }
                }
                if (terminal != null) route.Connect(terminal.SidewalkArrivalPosition, 29f);
            }
            if (poles.Count < 2)
                throw new InvalidOperationException("No clear lower-city electrical feeder leaves the substation.");
        }

        private sealed class CoreFeedRoutes
        {
            private const float PoleCost = 80f;
            private readonly CityLayout layout;
            private readonly IList<CityCoastalFacility> facilities;
            private readonly ClearanceContext clearance;
            private readonly List<Vector3> poles, forwards;
            private readonly List<Rect> solids;
            private readonly List<Vector2Int> connections;
            private readonly Vector3[] sites, bearings;
            private readonly int[] installed, parents, hops;
            private readonly float[] costs, estimates;
            private readonly bool[] visited;
            private readonly List<(int site, float length)>[] nearby;
            private readonly Dictionary<int, bool> visibility = new();
            private readonly List<int> path = new();

            internal CoreFeedRoutes(CityLayout layout, IList<CityCoastalFacility> facilities, ClearanceContext clearance,
                List<Vector3> candidates, List<Vector3> directions, List<Vector3> poles, List<Vector3> forwards,
                List<Rect> solids, List<Vector2Int> connections)
            {
                this.layout = layout; this.facilities = facilities; this.clearance = clearance;
                this.poles = poles; this.forwards = forwards; this.solids = solids; this.connections = connections;
                int count = candidates.Count + 1;
                sites = new Vector3[count]; bearings = new Vector3[count]; installed = new int[count];
                nearby = new List<(int, float)>[count]; costs = new float[count]; parents = new int[count];
                estimates = new float[count]; hops = new int[count]; visited = new bool[count];
                var localBuildings = new List<SolidVolume>();
                sites[0] = poles[0];
                for (int i = 0; i < count; i++)
                {
                    nearby[i] = new List<(int, float)>();
                    if (i == 0) continue;
                    installed[i] = -1; sites[i] = candidates[i - 1]; bearings[i] = directions[i - 1];
                    localBuildings.Clear();
                    Vector3 origin = sites[i];
                    var reach = Rect.MinMaxRect(origin.x - MaximumSpanLength, origin.z - MaximumSpanLength,
                        origin.x + MaximumSpanLength, origin.z + MaximumSpanLength);
                    foreach (var building in clearance.Solids)
                        if (reach.Overlaps(building.Footprint)) localBuildings.Add(building);
                    for (int j = 0; j < i; j++)
                    {
                        float squared = (sites[i] - sites[j]).sqrMagnitude;
                        if (squared < 16f || squared > MaximumSpanLength * MaximumSpanLength) continue;
                        if (CrossesFacade(sites[i], sites[j], localBuildings)) continue;
                        float distance = Mathf.Sqrt(squared);
                        nearby[i].Add((j, distance)); nearby[j].Add((i, distance));
                    }
                }
            }

            // A cheap exact test of the centre conductor removes impossible diagonal
            // shortcuts before the search. The remaining candidate routes still check
            // all three conductors and the road profile with the shared clearance rules.
            private static bool CrossesFacade(Vector3 a, Vector3 b, List<SolidVolume> buildings)
            {
                Vector3 delta = b - a;
                const float sag = .65f;
                float trough = .5f - delta.y / (8f * sag);
                foreach (var solid in buildings)
                {
                    float low = 0f, high = 1f;
                    Rect r = solid.Footprint;
                    if (!ClipAxis(a.x, delta.x, r.xMin, r.xMax, ref low, ref high) ||
                        !ClipAxis(a.z, delta.z, r.yMin, r.yMax, ref low, ref high)) continue;
                    float t = Mathf.Clamp(trough, low, high);
                    float height = a.y + PoleCableHeight + delta.y * t - 4f * sag * t * (1f - t);
                    if (height <= solid.Top) return true;
                }
                return false;
            }

            private static bool ClipAxis(float start, float delta, float min, float max, ref float low, ref float high)
            {
                if (Mathf.Abs(delta) < .0001f) return start >= min && start <= max;
                float left = (min - start) / delta, right = (max - start) / delta;
                low = Mathf.Max(low, Mathf.Min(left, right)); high = Mathf.Min(high, Mathf.Max(left, right));
                return low <= high;
            }

            internal bool Connect(Vector3 target, float radius)
            {
                bool reachable = false;
                for (int i = 0; i < sites.Length; i++)
                {
                    float dx = target.x - sites[i].x, dz = target.z - sites[i].z;
                    float remaining = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - radius);
                    estimates[i] = remaining * (1f + PoleCost / MaximumSpanLength);
                    if (remaining > 0f) continue;
                    if (installed[i] >= 0) return true;
                    reachable = true;
                }
                if (!reachable || poles.Count >= MaximumCorePoles) return false;

                // Lazy shortest path. Most geometrically nearby pairs never belong to a
                // prospective route. Only those on a complete route need the three actual
                // sagged conductors checked against buildings and profiled roads.
                while (FindPath())
                {
                    bool valid = true;
                    for (int i = path.Count - 1; i > 0; i--)
                        if (!Clear(path[i], path[i - 1])) valid = false;
                    if (!valid) continue;
                    int start = path[path.Count - 1];
                    if (start == 0 && bearings[0] == Vector3.zero && path.Count > 1)
                    {
                        bearings[0] = PlanarDirection(sites[0], sites[path[path.Count - 2]]);
                        forwards[0] = bearings[0];
                        // Only the source crossarm changed from its provisional bearing.
                        foreach (var edge in nearby[0]) visibility.Remove(Key(0, edge.site));
                    }
                    for (int i = path.Count - 2; i >= 0; i--)
                    {
                        int site = path[i];
                        installed[site] = poles.Count;
                        connections.Add(new Vector2Int(installed[path[i + 1]], poles.Count));
                        poles.Add(sites[site]); forwards.Add(bearings[site]); solids.Add(PoleFootprint(sites[site]));
                    }
                    return true;
                }
                return false;
            }

            private bool FindPath()
            {
                int remainingPoles = MaximumCorePoles - poles.Count;
                for (int i = 0; i < sites.Length; i++)
                {
                    costs[i] = installed[i] >= 0 ? 0f : float.PositiveInfinity;
                    parents[i] = -1; visited[i] = false; hops[i] = 0;
                }
                while (true)
                {
                    int here = -1; float best = float.PositiveInfinity;
                    for (int i = 0; i < sites.Length; i++)
                    {
                        float estimate = costs[i] + estimates[i];
                        if (!visited[i] && estimate < best) { best = estimate; here = i; }
                    }
                    if (here < 0) return false;
                    if (estimates[here] == 0f)
                    {
                        path.Clear();
                        for (int node = here; node >= 0; node = parents[node]) path.Add(node);
                        return true;
                    }
                    visited[here] = true;
                    if (hops[here] >= remainingPoles) continue;
                    foreach (var next in nearby[here])
                    {
                        if (visited[next.site] || installed[next.site] >= 0) continue;
                        float cost = costs[here] + next.length + PoleCost;
                        if (cost >= costs[next.site]) continue;
                        if (visibility.TryGetValue(Key(here, next.site), out bool clear) && !clear) continue;
                        costs[next.site] = cost; hops[next.site] = hops[here] + 1; parents[next.site] = here;
                    }
                }
            }

            private int Key(int a, int b) => Mathf.Min(a,b) * sites.Length + Mathf.Max(a,b);

            private bool Clear(int a, int b)
            {
                int key = Key(a, b);
                if (visibility.TryGetValue(key, out bool ok)) return ok;
                Vector3 forwardA = a == 0 && bearings[0] == Vector3.zero
                    ? PlanarDirection(sites[a], sites[b]) : bearings[a];
                ok = ClearBundle(layout, facilities, clearance, sites[a], sites[b], forwardA, bearings[b], out _);
                visibility.Add(key, ok);
                return ok;
            }

            private static Vector3 PlanarDirection(Vector3 start, Vector3 end) =>
                new Vector3(end.x - start.x, 0f, end.z - start.z).normalized;
        }
    }
}
