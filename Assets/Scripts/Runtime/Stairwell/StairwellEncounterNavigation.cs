using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded BFS on the second storey's existing support graph; no XZ shortcut across a void.</summary>
    internal sealed class StairwellEncounterNavigation
    {
        private readonly StairwellEncounterPlan plan;
        private readonly int[] parents, queue;
        private readonly bool[,] links;
        private readonly Vector3[,] portals;
        public StairwellEncounterNavigation(StairwellEncounterPlan plan)
        {
            this.plan = plan;
            int count = plan.Supports.Count;
            parents = new int[count]; queue = new int[count]; links = new bool[count, count]; portals = new Vector3[count, count];
            for (int i = 0; i < count; i++)
            for (int j = i + 1; j < count; j++)
            {
                Rect a = plan.Supports[i].Bounds, b = plan.Supports[j].Bounds;
                float x0 = Mathf.Max(a.xMin, b.xMin), x1 = Mathf.Min(a.xMax, b.xMax);
                float z0 = Mathf.Max(a.yMin, b.yMin), z1 = Mathf.Min(a.yMax, b.yMax);
                if (x1 < x0 || z1 < z0) continue;
                links[i, j] = links[j, i] = true;
                portals[i, j] = portals[j, i] = new Vector3((x0 + x1) * .5f,
                    StairwellEncounterPlan.Floor + PlayerFactory.GroundedRootOffset, (z0 + z1) * .5f);
            }
        }

        public bool TryWaypoint(Vector3 from, Vector3 target, out Vector3 waypoint)
        {
            waypoint = from;
            int start = plan.FindSupport(from), goal = plan.FindSupport(target);
            if (start < 0 || goal < 0) return false;
            if (start == goal) { waypoint = target; return true; }
            for (int i = 0; i < parents.Length; i++) parents[i] = -1;
            int head = 0, tail = 0;
            queue[tail++] = start; parents[start] = start;
            while (head < tail && parents[goal] < 0)
            {
                int node = queue[head++];
                for (int i = 0; i < parents.Length; i++)
                    if (parents[i] < 0 && links[node, i]) { parents[i] = node; queue[tail++] = i; }
            }
            if (parents[goal] < 0) return false;
            int next = goal;
            while (parents[next] != start) next = parents[next];
            waypoint = portals[start, next];
            // Enter the next rectangle rather than stopping indefinitely on its shared boundary.
            if ((waypoint - from).sqrMagnitude < .25f)
            {
                var support = plan.Supports[next];
                Vector3 centre = (support.Start + support.End) * .5f + Vector3.up * PlayerFactory.GroundedRootOffset;
                waypoint = next == goal ? target : centre;
            }
            return true;
        }
    }
}
