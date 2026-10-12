using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Surface-aware constraints and a small immutable-plan route graph. Physics owns visible collision.</summary>
    public sealed class WarehouseArenaArea : IWalkableArea
    {
        private const float MaximumStep = .23f;
        private List<int>[] neighbours = Array.Empty<List<int>>();
        private float[] routeCost = Array.Empty<float>();
        private int[] routePrevious = Array.Empty<int>();
        private bool[] routeClosed = Array.Empty<bool>();
        public WarehouseArenaPlan Plan { get; private set; }
        public WarehouseArenaArea(WarehouseArenaPlan plan) { ReplacePlan(plan); }

        public void ReplacePlan(WarehouseArenaPlan plan)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            neighbours = new List<int>[plan.NavigationNodes.Count];
            routeCost = new float[neighbours.Length]; routePrevious = new int[neighbours.Length]; routeClosed = new bool[neighbours.Length];
            for (int i = 0; i < neighbours.Length; i++) neighbours[i] = new List<int>();
            foreach (WarehouseArenaEdge edge in plan.NavigationEdges)
            { neighbours[edge.From].Add(edge.To); neighbours[edge.To].Add(edge.From); }
        }

        public bool Contains(Vector3 position, float radius = 0f) => TryGetHeight(position, out _, radius);

        public Vector3 Constrain(Vector3 currentPosition, Vector3 desiredPosition, float radius = 0f)
        {
            if (!Finite(desiredPosition) || radius < 0f) return currentPosition;
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, -12f + radius, 12f - radius);
            desiredPosition.z = Mathf.Clamp(desiredPosition.z, -9f + radius, 9f - radius);
            if (StepAllowed(currentPosition, desiredPosition, radius)) return desiredPosition;
            // Preserve sliding beside racks/rails instead of snapping the capsule around a corner.
            Vector3 alongX = new Vector3(desiredPosition.x, desiredPosition.y, currentPosition.z);
            if (StepAllowed(currentPosition, alongX, radius)) return alongX;
            Vector3 alongZ = new Vector3(currentPosition.x, desiredPosition.y, desiredPosition.z);
            if (StepAllowed(currentPosition, alongZ, radius)) return alongZ;
            return new Vector3(currentPosition.x, desiredPosition.y, currentPosition.z);
        }

        private bool StepAllowed(Vector3 current, Vector3 desired, float radius)
        {
            if (!TryGetHeight(current, out float before, radius) || !TryGetHeight(desired, out float after, radius)) return false;
            return Mathf.Abs(after - before) <= MaximumStep && Mathf.Abs(after - current.y) < .48f;
        }

        /// <summary>Chooses the surface nearest the query's feet, keeping the lower floor distinct from its gallery.</summary>
        public bool TryGetHeight(Vector3 position, out float height, float radius = 0f)
        {
            height = 0f;
            if (!Finite(position) || radius < 0f || Mathf.Abs(position.x) > 12f - radius || Mathf.Abs(position.z) > 9f - radius)
                return false;
            float best = float.PositiveInfinity;
            bool found = false;
            float floorClearance = float.PositiveInfinity;
            foreach (WarehouseArenaStair stair in Plan.Stairs)
                if (InRect(stair.Bounds, position.x, position.z)) floorClearance = Mathf.Min(floorClearance, stair.HeightAt(position.z) - .07f);
            // Low open treads leave no standing headroom. The first tread remains a normal climbable step.
            if (floorClearance > 1.85f || floorClearance < .12f)
                Consider(position, radius, 0f, ref height, ref best, ref found);
            if (CircleOnUpper(position.x, position.z, radius))
                Consider(position, radius, WarehouseArenaPlan.UpperHeight, ref height, ref best, ref found);
            foreach (WarehouseArenaStair stair in Plan.Stairs)
            {
                Rect bounds = stair.Bounds;
                if (position.x < bounds.xMin + radius || position.x > bounds.xMax - radius ||
                    position.z < bounds.yMin - .03f || position.z > bounds.yMax + .03f) continue;
                Consider(position, radius, stair.HeightAt(position.z), ref height, ref best, ref found);
            }
            return found;
        }

        private void Consider(Vector3 position, float radius, float candidate, ref float height, ref float best, ref bool found)
        {
            if (Blocked(position.x, position.z, candidate, radius)) return;
            float distance = Mathf.Abs(position.y - candidate);
            if (distance >= best) return;
            height = candidate; best = distance; found = true;
        }

        private bool Blocked(float x, float z, float floor, float radius)
        {
            foreach (WarehouseArenaObstacle obstacle in Plan.Obstacles)
            {
                Bounds bounds = obstacle.Bounds;
                if (floor >= bounds.max.y - .01f || floor + 1.75f <= bounds.min.y) continue;
                if (x > bounds.min.x - radius && x < bounds.max.x + radius &&
                    z > bounds.min.z - radius && z < bounds.max.z + radius) return true;
            }
            return false;
        }

        private bool CircleOnUpper(float x, float z, float radius)
        {
            if (!OnUpper(x,z)) return false;
            for (int i = 0; i < 8 && radius > 0f; i++)
            {
                float angle = i * Mathf.PI * .25f;
                float sx = x + Mathf.Cos(angle) * radius, sz = z + Mathf.Sin(angle) * radius;
                if (OnUpper(sx,sz)) continue;
                bool joinsStair = false;
                foreach (WarehouseArenaStair stair in Plan.Stairs)
                    if (sx >= stair.Foot.x - 1f && sx <= stair.Foot.x + 1f && sz >= 5.1f && sz <= 5.51f)
                    { joinsStair = true; break; }
                if (!joinsStair) return false;
            }
            return true;
        }

        private bool OnUpper(float x, float z)
        {
            foreach (Vector3 platform in Plan.Platforms)
                if (Mathf.Abs(x - platform.x) <= 1.5001f && Mathf.Abs(z - platform.z) <= 1.5001f) return true;
            return false;
        }

        public bool SameSurface(Vector3 from, Vector3 to) =>
            TryGetHeight(from, out float a) && TryGetHeight(to, out float b) && Mathf.Abs(a - b) < .24f;

        /// <summary>Direct travel must stay clear at feet and capsule height; deck edges cannot become shortcuts.</summary>
        public bool CanTraverse(Vector3 from, Vector3 to, float radius = .36f)
        {
            if (!TryGetHeight(from, out float previous, radius) || !TryGetHeight(to, out float target, radius)) return false;
            if (Mathf.Abs(previous - from.y) > .25f || Mathf.Abs(target - to.y) > .25f) return false;
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from,to) / .15f));
            for (int i = 1; i <= samples; i++)
            {
                Vector3 position = Vector3.Lerp(from,to,i/(float)samples);
                if (!TryGetHeight(position, out float next, radius) || Mathf.Abs(next - position.y) > .24f ||
                    Mathf.Abs(next - previous) > MaximumStep) return false;
                previous = next;
            }
            return true;
        }

        public bool FindRoute(Vector3 from, Vector3 to, List<Vector3> destination, float radius = .36f)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (!TryGetHeight(from,out float fromHeight,radius) || !TryGetHeight(to,out float toHeight,radius)) return false;
            from.y = fromHeight; to.y = toHeight;
            if (CanTraverse(from,to,radius)) { destination.Add(to); return true; }
            int count = Plan.NavigationNodes.Count;
            float[] cost = routeCost; int[] previous = routePrevious; bool[] closed = routeClosed;
            int end = -1;
            for (int i = 0; i < count; i++)
            {
                cost[i] = float.PositiveInfinity; previous[i] = -1; closed[i] = false;
                if ((from - Plan.NavigationNodes[i]).sqrMagnitude <= 9f && CanTraverse(from,Plan.NavigationNodes[i],radius))
                    cost[i] = Vector3.Distance(from,Plan.NavigationNodes[i]);
            }
            for (int iteration = 0; iteration < count; iteration++)
            {
                int current = -1; float best = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    float estimate = cost[i] + Vector3.Distance(Plan.NavigationNodes[i],to);
                    if (!closed[i] && estimate < best) { best = estimate; current = i; }
                }
                if (current < 0) break;
                if ((Plan.NavigationNodes[current] - to).sqrMagnitude <= 9f && CanTraverse(Plan.NavigationNodes[current],to,radius))
                { end = current; break; }
                closed[current] = true;
                foreach (int neighbour in neighbours[current])
                {
                    float next = cost[current] + Vector3.Distance(Plan.NavigationNodes[current],Plan.NavigationNodes[neighbour]);
                    if (next >= cost[neighbour]) continue;
                    cost[neighbour] = next; previous[neighbour] = current;
                }
            }
            if (end < 0) return false;
            for (int i = end; i >= 0; i = previous[i]) destination.Add(Plan.NavigationNodes[i]);
            destination.Reverse(); destination.Add(to);
            // Skip visible same-surface intermediate nodes but retain every stair turn and floor change.
            Vector3 start = from;
            for (int i = 0; i + 1 < destination.Count;)
            {
                if (CanTraverse(start,destination[i+1],radius)) destination.RemoveAt(i);
                else { start = destination[i]; i++; }
            }
            return true;
        }

        public bool TryGetNextStep(Vector3 from, Vector3 to, out Vector3 next, float radius = .36f)
        {
            var route = new List<Vector3>();
            bool found = FindRoute(from,to,route,radius);
            next = found ? route[0] : from;
            return found;
        }

        private static bool InRect(Rect rect, float x, float z) => x >= rect.xMin && x <= rect.xMax && z >= rect.yMin && z <= rect.yMax;
        private static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) &&
            !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);
    }
}
