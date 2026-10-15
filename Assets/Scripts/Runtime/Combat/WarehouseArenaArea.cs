using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Feet-aware storey selection; bounded spatial lookups and reusable route scratch.</summary>
    public sealed class WarehouseArenaArea : IWalkableArea
    {
        private const float MaximumStep = .23f, Headroom = 1.85f, SurfaceCapture = .62f, ObstacleCellSize = 3f;
        private readonly HashSet<Vector3Int> deckTiles = new HashSet<Vector3Int>();
        private readonly Dictionary<Vector2Int, List<int>> obstacleCells = new Dictionary<Vector2Int, List<int>>();
        private readonly List<Vector3> nextStepRoute = new List<Vector3>(64);
        private List<int>[] neighbours = Array.Empty<List<int>>();
        private float[] routeCost = Array.Empty<float>(), routeEstimate = Array.Empty<float>();
        private int[] routePrevious = Array.Empty<int>(), routeHeap = Array.Empty<int>(), heapIndex = Array.Empty<int>();
        private bool[] routeClosed = Array.Empty<bool>();
        private int heapCount;
        private readonly WarehouseGateState gateState;
        public WarehouseArenaPlan Plan { get; private set; }
        public WarehouseArenaArea(WarehouseArenaPlan plan, WarehouseGateState gateState = null)
        { this.gateState = gateState; ReplacePlan(plan); }

        public void ReplacePlan(WarehouseArenaPlan plan)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            deckTiles.Clear(); obstacleCells.Clear(); nextStepRoute.Clear();
            foreach (Vector3 tile in plan.Platforms)
                deckTiles.Add(new Vector3Int(Mathf.RoundToInt((tile.x + 13.5f) / 3f),
                    Mathf.RoundToInt(tile.y / WarehouseArenaPlan.LevelRise), Mathf.RoundToInt((tile.z + 10.5f) / 3f)));
            for (int i = 0; i < plan.Obstacles.Count; i++)
            {
                Bounds bounds = plan.Obstacles[i].Bounds;
                for (int z = Mathf.FloorToInt(bounds.min.z / ObstacleCellSize); z <= Mathf.FloorToInt(bounds.max.z / ObstacleCellSize); z++)
                    for (int x = Mathf.FloorToInt(bounds.min.x / ObstacleCellSize); x <= Mathf.FloorToInt(bounds.max.x / ObstacleCellSize); x++)
                    {
                        var key = new Vector2Int(x,z);
                        if (!obstacleCells.TryGetValue(key, out List<int> indices))
                        { indices = new List<int>(8); obstacleCells.Add(key,indices); }
                        indices.Add(i);
                    }
            }
            int count = plan.NavigationNodes.Count;
            neighbours = new List<int>[count]; routeCost = new float[count]; routeEstimate = new float[count];
            routePrevious = new int[count]; routeClosed = new bool[count]; routeHeap = new int[count]; heapIndex = new int[count];
            for (int i = 0; i < count; i++) neighbours[i] = new List<int>(8);
            foreach (WarehouseArenaEdge edge in plan.NavigationEdges)
            { neighbours[edge.From].Add(edge.To); neighbours[edge.To].Add(edge.From); }
        }

        public bool Contains(Vector3 position, float radius = 0f) => TryGetHeight(position, out _, radius);

        public Vector3 Constrain(Vector3 currentPosition, Vector3 desiredPosition, float radius = 0f)
        {
            if (!Finite(desiredPosition) || radius < 0f) return currentPosition;
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, -WarehouseArenaPlan.HalfWidth + radius, WarehouseArenaPlan.HalfWidth - radius);
            desiredPosition.z = Mathf.Clamp(desiredPosition.z, -WarehouseArenaPlan.HalfDepth + radius, WarehouseArenaPlan.HalfDepth - radius);
            if (StepAllowed(currentPosition, desiredPosition, radius)) return desiredPosition;
            Vector3 alongX = new Vector3(desiredPosition.x, desiredPosition.y, currentPosition.z);
            if (StepAllowed(currentPosition, alongX, radius)) return alongX;
            Vector3 alongZ = new Vector3(currentPosition.x, desiredPosition.y, desiredPosition.z);
            if (StepAllowed(currentPosition, alongZ, radius)) return alongZ;
            return new Vector3(currentPosition.x, desiredPosition.y, currentPosition.z);
        }

        private bool StepAllowed(Vector3 current, Vector3 desired, float radius)
        {
            if (!TryGetHeight(current, out float before, radius) || !TryGetHeight(desired, out float after, radius) ||
                Mathf.Abs(after - before) > MaximumStep || Mathf.Abs(after - current.y) >= .48f) return false;
            // Impulses may be longer than a frame of walking. Sample them too, so thin partitions remain solid.
            current.y = before; desired.y = after;
            return CanTraverse(current, desired, radius);
        }

        /// <summary>Choose a nearby supporting surface from feet height, never a different floor through a shaft.</summary>
        public bool TryGetHeight(Vector3 position, out float height, float radius = 0f)
        {
            height = 0f;
            if (!Finite(position) || radius < 0f || Mathf.Abs(position.x) > WarehouseArenaPlan.HalfWidth - radius ||
                Mathf.Abs(position.z) > WarehouseArenaPlan.HalfDepth - radius) return false;
            float best = float.PositiveInfinity; bool found = false;
            for (int level = 0; level <= 2; level++)
            {
                float candidate = level * WarehouseArenaPlan.LevelRise;
                if (Mathf.Abs(candidate - position.y) > SurfaceCapture || !CircleOnSurface(position.x,position.z,level,radius)) continue;
                Consider(position, radius, candidate, ref height, ref best, ref found);
            }
            foreach (WarehouseArenaStair stair in Plan.Stairs)
            {
                Rect bounds = stair.Bounds;
                // The side stringers sit slightly inside the tread edge; the capsule must clear those too.
                float inside = (WarehouseArenaStair.Width-WarehouseArenaStair.ClearWidth) * .5f;
                if (position.x < bounds.xMin + inside + radius || position.x > bounds.xMax - inside - radius ||
                    position.z < bounds.yMin - .03f || position.z > bounds.yMax + .03f) continue;
                float candidate = stair.HeightAt(position.z);
                if (Mathf.Abs(candidate - position.y) <= SurfaceCapture)
                    Consider(position, radius, candidate, ref height, ref best, ref found);
            }
            return found;
        }

        private void Consider(Vector3 position, float radius, float candidate, ref float height, ref float best, ref bool found)
        {
            if (Blocked(position.x,position.z,candidate,radius) || !HasHeadroom(position.x,position.z,candidate)) return;
            float distance = Mathf.Abs(position.y - candidate);
            if (distance >= best) return;
            height = candidate; best = distance; found = true;
        }

        private bool HasHeadroom(float x, float z, float floor)
        {
            foreach (WarehouseArenaStair stair in Plan.Stairs)
            {
                if (!InRect(stair.Bounds,x,z)) continue;
                float underside = stair.HeightAt(z) - .08f;
                // The first tread is a normal step. Low treads above a lower standing floor are not a tunnel.
                if (underside > floor + .12f && underside < floor + Headroom) return false;
            }
            return floor + Headroom <= WarehouseArenaPlan.CeilingHeight;
        }

        private bool Blocked(float x, float z, float floor, float radius)
        {
            for (int cz = Mathf.FloorToInt((z-radius) / ObstacleCellSize); cz <= Mathf.FloorToInt((z+radius) / ObstacleCellSize); cz++)
                for (int cx = Mathf.FloorToInt((x-radius) / ObstacleCellSize); cx <= Mathf.FloorToInt((x+radius) / ObstacleCellSize); cx++)
                {
                    if (!obstacleCells.TryGetValue(new Vector2Int(cx,cz),out List<int> indices)) continue;
                    foreach (int index in indices)
                    {
                        Bounds bounds = Plan.Obstacles[index].Bounds;
                        if (Plan.Obstacles[index].Kind == WarehouseArenaObstacleKind.Gate && gateState != null)
                            bounds.center += Vector3.up * (gateState.Progress * WarehouseGateController.MaximumLift);
                        if (floor >= bounds.max.y - .01f || floor + Headroom <= bounds.min.y) continue;
                        if (x > bounds.min.x - radius && x < bounds.max.x + radius &&
                            z > bounds.min.z - radius && z < bounds.max.z + radius) return true;
                    }
                }
            return false;
        }

        private bool CircleOnSurface(float x, float z, int level, float radius)
        {
            if (!PointOnSurface(x,z,level)) return false;
            for (int i = 0; i < 8 && radius > 0f; i++)
            {
                float angle = i * Mathf.PI * .25f;
                float sx = x + Mathf.Cos(angle) * radius, sz = z + Mathf.Sin(angle) * radius;
                if (PointOnSurface(sx,sz,level)) continue;
                bool joinsStair = false;
                foreach (WarehouseArenaStair stair in Plan.Stairs)
                {
                    if (sx < stair.Bounds.xMin || sx > stair.Bounds.xMax) continue;
                    float floor = level * WarehouseArenaPlan.LevelRise;
                    bool atTop = Mathf.Abs(stair.Top.y-floor) < .01f && sz >= stair.Top.z-.42f && sz <= stair.Top.z+.01f;
                    bool atFoot = Mathf.Abs(stair.Foot.y-floor) < .01f && sz >= stair.Foot.z-.01f && sz <= stair.Foot.z+.42f;
                    if (!atTop && !atFoot) continue;
                    joinsStair = true; break;
                }
                if (!joinsStair) return false;
            }
            return true;
        }

        private bool PointOnSurface(float x, float z, int level)
        {
            if (level == 0) return Mathf.Abs(x) <= WarehouseArenaPlan.HalfWidth && Mathf.Abs(z) <= WarehouseArenaPlan.HalfDepth;
            float columnPosition = (x + WarehouseArenaPlan.HalfWidth) / WarehouseArenaPlan.TileSize;
            float rowPosition = (z + WarehouseArenaPlan.HalfDepth) / WarehouseArenaPlan.TileSize;
            int column = Mathf.FloorToInt(columnPosition), row = Mathf.FloorToInt(rowPosition);
            bool seamX = Mathf.Abs(columnPosition - column) < .0001f, seamZ = Mathf.Abs(rowPosition - row) < .0001f;
            for (int dx = 0; dx >= (seamX ? -1 : 0); dx--)
                for (int dz = 0; dz >= (seamZ ? -1 : 0); dz--)
                    if (deckTiles.Contains(new Vector3Int(column + dx,level,row + dz))) return true;
            return false;
        }

        public bool SameSurface(Vector3 from, Vector3 to) =>
            TryGetHeight(from,out float a) && TryGetHeight(to,out float b) && Mathf.Abs(a-b) < .24f;

        public bool CanTraverse(Vector3 from, Vector3 to, float radius = .36f)
        {
            if (!TryGetHeight(from,out float previous,radius) || !TryGetHeight(to,out float target,radius)) return false;
            if (Mathf.Abs(previous-from.y) > .25f || Mathf.Abs(target-to.y) > .25f) return false;
            int samples = Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to) / .15f));
            for (int i = 1; i <= samples; i++)
            {
                Vector3 position = Vector3.Lerp(from,to,i/(float)samples);
                if (!TryGetHeight(position,out float next,radius) || Mathf.Abs(next-position.y) > .24f ||
                    Mathf.Abs(next-previous) > MaximumStep) return false;
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
            int count = Plan.NavigationNodes.Count, end = -1;
            heapCount = 0;
            for (int i = 0; i < count; i++)
            {
                routeCost[i] = float.PositiveInfinity; routePrevious[i] = -1; routeClosed[i] = false; heapIndex[i] = -1;
                routeEstimate[i] = Vector3.Distance(Plan.NavigationNodes[i],to);
            }
            for (int i = 0; i < count; i++)
                if ((from-Plan.NavigationNodes[i]).sqrMagnitude <= 9f && CanTraverse(from,Plan.NavigationNodes[i],radius))
                { routeCost[i] = Vector3.Distance(from,Plan.NavigationNodes[i]); PushOrDecrease(i); }
            while (heapCount > 0)
            {
                int current = PopNearest();
                if ((Plan.NavigationNodes[current]-to).sqrMagnitude <= 9f && CanTraverse(Plan.NavigationNodes[current],to,radius))
                { end = current; break; }
                routeClosed[current] = true;
                foreach (int neighbour in neighbours[current])
                {
                    if (routeClosed[neighbour]) continue;
                    if (radius > .3601f && !CanTraverse(Plan.NavigationNodes[current],Plan.NavigationNodes[neighbour],radius)) continue;
                    float next = routeCost[current] + Vector3.Distance(Plan.NavigationNodes[current],Plan.NavigationNodes[neighbour]);
                    if (next >= routeCost[neighbour]) continue;
                    routeCost[neighbour] = next; routePrevious[neighbour] = current; PushOrDecrease(neighbour);
                }
            }
            if (end < 0) return false;
            for (int i = end; i >= 0; i = routePrevious[i]) destination.Add(Plan.NavigationNodes[i]);
            destination.Reverse(); destination.Add(to);
            Vector3 start = from;
            for (int i = 0; i + 1 < destination.Count;)
            {
                if (CanTraverse(start,destination[i+1],radius)) destination.RemoveAt(i);
                else { start = destination[i]; i++; }
            }
            return true;
        }

        private bool Before(int a, int b)
        {
            float difference = (routeCost[a] + routeEstimate[a]) - (routeCost[b] + routeEstimate[b]);
            return difference < 0f || difference == 0f && a < b;
        }

        private void PushOrDecrease(int node)
        {
            int index = heapIndex[node];
            if (index < 0) { index = heapCount++; routeHeap[index] = node; heapIndex[node] = index; }
            while (index > 0)
            {
                int parent = (index-1) / 2;
                if (!Before(node,routeHeap[parent])) break;
                routeHeap[index] = routeHeap[parent]; heapIndex[routeHeap[index]] = index; index = parent;
            }
            routeHeap[index] = node; heapIndex[node] = index;
        }

        private int PopNearest()
        {
            int result = routeHeap[0], node = routeHeap[--heapCount]; heapIndex[result] = -1;
            if (heapCount == 0) return result;
            int index = 0;
            while (index * 2 + 1 < heapCount)
            {
                int child = index * 2 + 1;
                if (child + 1 < heapCount && Before(routeHeap[child+1],routeHeap[child])) child++;
                if (!Before(routeHeap[child],node)) break;
                routeHeap[index] = routeHeap[child]; heapIndex[routeHeap[index]] = index; index = child;
            }
            routeHeap[index] = node; heapIndex[node] = index;
            return result;
        }

        public bool TryGetNextStep(Vector3 from, Vector3 to, out Vector3 next, float radius = .36f)
        {
            bool found = FindRoute(from,to,nextStepRoute,radius);
            next = found ? nextStepRoute[0] : from;
            return found;
        }

        private static bool InRect(Rect rect, float x, float z) => x >= rect.xMin && x <= rect.xMax && z >= rect.yMin && z <= rect.yMax;
        private static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) &&
            !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);
    }
}
