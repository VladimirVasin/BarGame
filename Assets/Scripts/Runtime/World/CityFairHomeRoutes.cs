using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The fair's aisles join the west street. Shared pavement and
    /// entrance plans carry each participant from there into a different house.</summary>
    internal sealed class CityFairHomeRoutes
    {
        private readonly CityFairPlan fair;
        private readonly CityBuildingEntrancePlan entrances;
        private readonly CityStreetSupportPlan streets;
        private readonly CityWalkingPath[] homes;
        internal readonly Vector3 Gate;

        internal CityFairHomeRoutes(CityFairPlan fair, int count)
        {
            this.fair = fair;
            CityLayout layout = fair.Layout;
            entrances = CityBuildingEntrancePlan.ForLayout(layout);
            streets = CityStreetSurfacePlanner.Create(layout).SupportPlan;
            Gate = Ground(new Vector3(fair.Bounds.xMin - .4f, 0f, fair.CenterPathNorth.z));
            CityPedestrianPlan graph = CityLayoutCache.GetOrCreatePedestrianPlan(layout, layout.Seed);
            if (!CityBusStopWaitPlanner.TryFindNearestNode(graph, Gate, out int start))
                throw new InvalidOperationException("The fair's west exit has no street access.");
            float[] distances = CityBusStopWaitPlanner.CreateNodeDistances(graph, start);
            var candidates = new List<CityBuildingEntranceDescriptor>(entrances.Entrances);
            candidates.Sort((a, b) => (a.Threshold - Gate).sqrMagnitude.CompareTo((b.Threshold - Gate).sqrMagnitude));
            homes = new CityWalkingPath[count];
            var nearest = new List<(float length, int node, CityBuildingEntranceDescriptor house)>(count + 1);
            foreach (var house in candidates)
            {
                if (house.Lot.IsPlayerHome) continue;
                if (nearest.Count == count && Vector3.Distance(house.Threshold, Gate) > nearest[count - 1].length) break;
                Vector3 pavement = house.Points[house.Points.Count - 1];
                int node = PavementNode(graph, distances, pavement);
                if (node < 0) continue;
                float length = Vector3.Distance(Gate, graph.Nodes[start].Position) + distances[node]
                    + Vector3.Distance(graph.Nodes[node].Position, pavement);
                for (int i = 1; i < house.Points.Count; i++) length += Vector3.Distance(house.Points[i - 1], house.Points[i]);
                int at = nearest.Count;
                while (at > 0 && nearest[at - 1].length > length) at--;
                if (at >= count) continue;
                nearest.Insert(at, (length, node, house));
                if (nearest.Count > count) nearest.RemoveAt(count);
            }
            if (nearest.Count != count)
                throw new InvalidOperationException("Every fair participant requires a separate reachable residential doorway.");
            for (int h = 0; h < count; h++)
            {
                var (_, node, house) = nearest[h];
                // One distance field serves all houses. Trace back from the door
                // to the fair, then traverse those links in the reverse order.
                var route = CityResidentWorld.BuildRoute(graph, node, start, distances);
                var points = new List<Vector3> { Gate };
                GroundLeg(points, graph.Nodes[start].Position);
                for (int i = route.Links.Length - 1; i >= 0; i--)
                {
                    CityPedestrianLink link = graph.Links[route.Links[i]];
                    Vector3 a = graph.Nodes[route.Nodes[i + 1]].Position;
                    Vector3 b = graph.Nodes[route.Nodes[i]].Position;
                    if (link.Path == null) { Add(points, b); continue; }
                    bool forward = link.FirstNodeIndex == route.Nodes[i + 1];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(link.Path.Length / .5f));
                    for (int step = 1; step <= steps; step++)
                    {
                        float t = step / (float)steps;
                        Vector2 xz = link.Path.SampleDistance(link.Path.Length * (forward ? t : 1f - t)).Position;
                        float y = link.PathHeightSampler?.Invoke(xz) ?? Mathf.Lerp(a.y, b.y, t);
                        Add(points, new Vector3(xz.x, y, xz.y));
                    }
                }
                for (int i = house.Points.Count - 1; i > 0; i--) GroundLeg(points, house.Points[i]);
                Add(points, house.Threshold);
                Add(points, house.Threshold - house.Outward * (CityBuildingAssetProvider.EntranceDepth - .35f));
                homes[h] = new CityWalkingPath(points);
            }
        }

        private int PavementNode(CityPedestrianPlan graph, float[] distances, Vector3 pavement)
        {
            int best = -1;
            float nearest = CityBusStopWaitPlanner.MaximumNodeDistance * CityBusStopWaitPlanner.MaximumNodeDistance;
            for (int i = 0; i < distances.Length; i++)
            {
                if (float.IsInfinity(distances[i]) || graph.IsPrivateWorkNode(i)) continue;
                Vector3 node = graph.Nodes[i].Position;
                float squared = (node - pavement).sqrMagnitude;
                if (squared >= nearest) continue;
                int samples = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(squared) / .45f));
                bool paved = true;
                for (int step = 1; step <= samples; step++)
                {
                    Vector3 point = Vector3.Lerp(pavement, node, step / (float)samples);
                    if (CityStreetSurfacePlanner.TrySamplePublicSidewalkTop(fair.Layout, new Vector2(point.x, point.z),
                        out float y) && Mathf.Abs(y - point.y) < .30f) continue;
                    paved = false;
                    break;
                }
                if (paved) { best = i; nearest = squared; }
            }
            return best;
        }

        internal CityWalkingPath Home(int index) => homes[index];

        internal void Assign(int night, int[] order)
        {
            var random = new System.Random(unchecked(fair.Layout.Seed * 397 ^ night));
            for (int i = 0; i < order.Length; i++)
            {
                int j = random.Next(i + 1);
                order[i] = order[j];
                order[j] = i;
            }
        }

        internal CityWalkingPath ToGate(int slot, Vector3 origin)
        {
            var path = new List<Vector3> { origin };
            int vendorCount = fair.Stalls.Length;
            int child = slot - vendorCount - fair.AdultPositions.Length;
            if (slot < vendorCount)
            {
                // Walk sideways from behind the counter between its legs and the rear shelf.
                var stall = fair.Stalls[slot];
                float side = Mathf.Sign(fair.Bounds.center.x - stall.Position.x);
                GroundLeg(path, origin + Vector3.right * side * 1.95f);
            }
            if (child == 2)
                GroundLeg(path, new Vector3(fair.Bounds.xMax - .80f, 0f, origin.z));
            bool south = child == 1 || child < 0 && origin.z < fair.BoundaryZ;
            float lane = (south ? fair.CenterPathSouth.z : fair.CenterPathNorth.z) + (slot % 3 - 1) * .36f;
            GroundLeg(path, new Vector3(path[path.Count - 1].x, 0f, lane));
            GroundLeg(path, new Vector3(Gate.x, 0f, lane));
            GroundLeg(path, Gate);
            return new CityWalkingPath(path);
        }

        private void GroundLeg(List<Vector3> points, Vector3 goal)
        {
            Vector3 from = points[points.Count - 1];
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(new Vector2(from.x, from.z),
                new Vector2(goal.x, goal.z)) / .45f));
            for (int i = 1; i <= steps; i++) Add(points, Ground(Vector3.Lerp(from, goal, i / (float)steps)));
        }

        private Vector3 Ground(Vector3 point)
        {
            var xz = new Vector2(point.x, point.z);
            if (!entrances.TrySampleTop(xz, out point.y) && !streets.TrySampleTop(xz, out point.y, out _))
                point.y = fair.SampleGroundY(point);
            return point;
        }

        private static void Add(List<Vector3> points, Vector3 point)
        {
            if ((points[points.Count - 1] - point).sqrMagnitude > .000001f) points.Add(point);
        }
    }
}
