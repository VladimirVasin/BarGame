using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Feet-aware storey selection; bounded spatial lookups and reusable route scratch.</summary>
    public sealed class WarehouseArenaArea : IWalkableArea
    {
        internal const float DefaultHeadroom = 1.85f;
        internal const float MaximumStep = .23f;
        private const float SurfaceCapture = .62f, ObstacleCellSize = 3f;
        private readonly HashSet<Vector3Int> deckTiles = new HashSet<Vector3Int>();
        private readonly Dictionary<Vector2Int, List<int>> obstacleCells = new Dictionary<Vector2Int, List<int>>();
        private readonly List<Bounds> gateBounds = new List<Bounds>(1);
        private readonly List<Vector3> nextStepRoute = new List<Vector3>(64);
        private readonly struct Neighbour
        {
            public readonly int Node;
            public readonly bool GateSensitive;
            public Neighbour(int node, bool gateSensitive) { Node = node; GateSensitive = gateSensitive; }
        }
        private List<Neighbour>[] neighbours = Array.Empty<List<Neighbour>>();
        private float[] routeCost = Array.Empty<float>(), routeEstimate = Array.Empty<float>();
        private int[] routePrevious = Array.Empty<int>(), routeHeap = Array.Empty<int>(), heapIndex = Array.Empty<int>();
        private bool[] routeClosed = Array.Empty<bool>();
        private int heapCount;
        private readonly WarehouseGateState gateState;
        private readonly bool ignoreGate;
        public WarehouseArenaPlan Plan { get; private set; }
        public float GateProgress => gateState != null ? gateState.Progress : 0f;
        public WarehouseArenaArea(WarehouseArenaPlan plan, WarehouseGateState gateState = null)
        { this.gateState = gateState; ReplacePlan(plan); }
        // The retained graph owns static topology; its gate-sensitive edges are
        // checked against the actual gate height for each route query.
        internal WarehouseArenaArea(WarehouseArenaPlan plan, bool ignoreGate)
        { this.ignoreGate = ignoreGate; ReplacePlan(plan); }

        public void ReplacePlan(WarehouseArenaPlan plan)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            deckTiles.Clear(); obstacleCells.Clear(); gateBounds.Clear(); nextStepRoute.Clear();
            foreach (Vector3 tile in plan.Platforms)
                deckTiles.Add(new Vector3Int(Mathf.RoundToInt((tile.x + 13.5f) / 3f),
                    Mathf.RoundToInt(tile.y / WarehouseArenaPlan.LevelRise), Mathf.RoundToInt((tile.z + 10.5f) / 3f)));
            for (int i = 0; i < plan.Obstacles.Count; i++)
            {
                Bounds bounds = plan.Obstacles[i].Bounds;
                if (plan.Obstacles[i].Kind == WarehouseArenaObstacleKind.Gate) gateBounds.Add(bounds);
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
            neighbours = new List<Neighbour>[count]; routeCost = new float[count]; routeEstimate = new float[count];
            routePrevious = new int[count]; routeClosed = new bool[count]; routeHeap = new int[count]; heapIndex = new int[count];
            for (int i = 0; i < count; i++) neighbours[i] = new List<Neighbour>(8);
            foreach (WarehouseArenaEdge edge in plan.NavigationEdges)
            {
                bool gateSensitive = MayCrossGate(plan.NavigationNodes[edge.From], plan.NavigationNodes[edge.To]);
                neighbours[edge.From].Add(new Neighbour(edge.To, gateSensitive));
                neighbours[edge.To].Add(new Neighbour(edge.From, gateSensitive));
            }
        }

        private bool MayCrossGate(Vector3 from, Vector3 to)
        {
            foreach (Bounds bounds in gateBounds)
            {
                if (Mathf.Max(from.x, to.x) >= bounds.min.x - .36f && Mathf.Min(from.x, to.x) <= bounds.max.x + .36f &&
                    Mathf.Max(from.z, to.z) >= bounds.min.z - .36f && Mathf.Min(from.z, to.z) <= bounds.max.z + .36f)
                    return true;
            }
            return false;
        }

        public bool Contains(Vector3 position, float radius = 0f) => TryGetHeight(position, out _, radius);

        public Vector3 Constrain(Vector3 currentPosition, Vector3 desiredPosition, float radius = 0f)
            => Constrain(currentPosition, desiredPosition, radius, DefaultHeadroom);

        internal Vector3 Constrain(Vector3 currentPosition, Vector3 desiredPosition, float radius, float height,
            float obstacleRadius = -1f)
        {
            if (!Finite(desiredPosition) || radius < 0f) return currentPosition;
            obstacleRadius = Mathf.Max(radius, obstacleRadius);
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, -WarehouseArenaPlan.HalfWidth + obstacleRadius, WarehouseArenaPlan.HalfWidth - obstacleRadius);
            desiredPosition.z = Mathf.Clamp(desiredPosition.z, -WarehouseArenaPlan.HalfDepth + obstacleRadius, WarehouseArenaPlan.HalfDepth - obstacleRadius);
            if (StepAllowed(currentPosition, desiredPosition, radius, height, obstacleRadius)) return desiredPosition;
            Vector3 alongX = new Vector3(desiredPosition.x, desiredPosition.y, currentPosition.z);
            if (StepAllowed(currentPosition, alongX, radius, height, obstacleRadius)) return alongX;
            Vector3 alongZ = new Vector3(currentPosition.x, desiredPosition.y, desiredPosition.z);
            if (StepAllowed(currentPosition, alongZ, radius, height, obstacleRadius)) return alongZ;
            return new Vector3(currentPosition.x, desiredPosition.y, currentPosition.z);
        }

        private bool StepAllowed(Vector3 current, Vector3 desired, float radius, float height, float obstacleRadius)
        {
            if (!TryGetHeight(current, out float before, radius, height, obstacleRadius) ||
                !TryGetHeight(desired, out float after, radius, height, obstacleRadius) ||
                Mathf.Abs(after - before) > MaximumStep || Mathf.Abs(after - current.y) >= .48f) return false;
            // Impulses may be longer than a frame of walking. Sample them too, so thin partitions remain solid.
            current.y = before; desired.y = after;
            return CanTraverse(current, desired, radius, height, obstacleRadius);
        }

        /// <summary>Choose a nearby supporting surface from feet height, never a different floor through a shaft.</summary>
        public bool TryGetHeight(Vector3 position, out float height, float radius = 0f,
            float clearanceHeight = DefaultHeadroom, float obstacleRadius = -1f)
        {
            height = 0f;
            obstacleRadius = Mathf.Max(radius, obstacleRadius);
            if (!Finite(position) || radius < 0f || Mathf.Abs(position.x) > WarehouseArenaPlan.HalfWidth - obstacleRadius ||
                Mathf.Abs(position.z) > WarehouseArenaPlan.HalfDepth - obstacleRadius) return false;
            float best = float.PositiveInfinity; bool found = false;
            for (int level = 0; level <= 2; level++)
            {
                float candidate = level * WarehouseArenaPlan.LevelRise;
                if (Mathf.Abs(candidate - position.y) > SurfaceCapture || !CircleOnSurface(position.x,position.z,level,radius)) continue;
                Consider(position, obstacleRadius, clearanceHeight, candidate, ref height, ref best, ref found);
            }
            foreach (WarehouseArenaStair stair in Plan.Stairs)
            {
                Rect bounds = stair.Bounds;
                // Feet retain their ordinary support radius, but the full
                // carried width must also clear the side guards/stringers.
                float inside = (WarehouseArenaStair.Width-WarehouseArenaStair.ClearWidth) * .5f;
                if (position.x < bounds.xMin + inside + obstacleRadius || position.x > bounds.xMax - inside - obstacleRadius ||
                    position.z < bounds.yMin - .03f || position.z > bounds.yMax + .03f) continue;
                if (!stair.HasTreadAt(position.z)) continue;
                float candidate = stair.HeightAt(position.z);
                if (Mathf.Abs(candidate - position.y) <= SurfaceCapture)
                    Consider(position, obstacleRadius, clearanceHeight, candidate, ref height, ref best, ref found);
            }
            return found;
        }

        private void Consider(Vector3 position, float obstacleRadius, float clearanceHeight, float candidate,
            ref float height, ref float best, ref bool found)
        {
            if (Blocked(position.x,position.z,candidate,obstacleRadius,clearanceHeight) ||
                !HasHeadroom(position.x,position.z,candidate,clearanceHeight)) return;
            float distance = Mathf.Abs(position.y - candidate);
            if (distance >= best) return;
            height = candidate; best = distance; found = true;
        }

        private bool HasHeadroom(float x, float z, float floor, float clearanceHeight)
        {
            foreach (WarehouseArenaStair stair in Plan.Stairs)
            {
                if (!InRect(stair.Bounds,x,z)) continue;
                if (!stair.HasTreadAt(z)) continue;
                float underside = stair.HeightAt(z) - .08f;
                // The first tread is a normal step. Low treads above a lower standing floor are not a tunnel.
                if (underside > floor + .12f && underside < floor + clearanceHeight) return false;
            }
            return floor + clearanceHeight <= WarehouseArenaPlan.CeilingHeight;
        }

        private bool Blocked(float x, float z, float floor, float radius, float clearanceHeight)
        {
            for (int cz = Mathf.FloorToInt((z-radius) / ObstacleCellSize); cz <= Mathf.FloorToInt((z+radius) / ObstacleCellSize); cz++)
                for (int cx = Mathf.FloorToInt((x-radius) / ObstacleCellSize); cx <= Mathf.FloorToInt((x+radius) / ObstacleCellSize); cx++)
                {
                    if (!obstacleCells.TryGetValue(new Vector2Int(cx,cz),out List<int> indices)) continue;
                    foreach (int index in indices)
                    {
                        if (ignoreGate && Plan.Obstacles[index].Kind == WarehouseArenaObstacleKind.Gate) continue;
                        Bounds bounds = Plan.Obstacles[index].Bounds;
                        if (Plan.Obstacles[index].Kind == WarehouseArenaObstacleKind.Gate && gateState != null)
                            bounds.center += Vector3.up * (gateState.Progress * WarehouseGateController.MaximumLift);
                        if (floor >= bounds.max.y - .01f || floor + clearanceHeight <= bounds.min.y) continue;
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

        public bool SameSurface(Vector3 from, Vector3 to, float radius = 0f, float height = DefaultHeadroom) =>
            TryGetHeight(from,out float a,radius,height) && TryGetHeight(to,out float b,radius,height) && Mathf.Abs(a-b) < .24f;

        public bool CanTraverse(Vector3 from, Vector3 to, float radius = .36f, float height = DefaultHeadroom,
            float obstacleRadius = -1f)
        {
            if (!TryGetHeight(from,out float previous,radius,height,obstacleRadius) ||
                !TryGetHeight(to,out float target,radius,height,obstacleRadius)) return false;
            if (Mathf.Abs(previous-from.y) > .25f || Mathf.Abs(target-to.y) > .25f) return false;
            int samples = Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to) / .15f));
            for (int i = 1; i <= samples; i++)
            {
                Vector3 position = Vector3.Lerp(from,to,i/(float)samples);
                if (!TryGetHeight(position,out float next,radius,height,obstacleRadius) || Mathf.Abs(next-position.y) > .24f ||
                    Mathf.Abs(next-previous) > MaximumStep) return false;
                previous = next;
            }
            return true;
        }

        public bool FindRoute(Vector3 from, Vector3 to, List<Vector3> destination, float radius = .36f, float height = DefaultHeadroom)
            => FindRouteInternal(from, to, destination, false, out _, radius, height, radius);

        /// <summary>Keep exact routes exact; otherwise approach the best reachable supported point without snapping the target to another storey.</summary>
        public bool FindApproachRoute(Vector3 from, Vector3 target, List<Vector3> destination, out bool reachesTarget,
            float radius = .36f, float height = DefaultHeadroom, float obstacleRadius = -1f)
            => FindRouteInternal(from, target, destination, true, out reachesTarget, radius, height, obstacleRadius);

        private bool FindRouteInternal(Vector3 from, Vector3 to, List<Vector3> destination, bool allowPartial,
            out bool reachesTarget, float radius, float height, float obstacleRadius)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            reachesTarget = false;
            obstacleRadius = Mathf.Max(radius, obstacleRadius);
            if (!Finite(to) || !TryGetHeight(from,out float fromHeight,radius,height,obstacleRadius)) return false;
            bool supportedTarget = TryGetHeight(to,out float toHeight,radius,height,obstacleRadius);
            if (!supportedTarget && !allowPartial) return false;
            from.y = fromHeight;
            if (supportedTarget) to.y = toHeight;
            if (supportedTarget && CanTraverse(from,to,radius,height,obstacleRadius))
            { destination.Add(to); reachesTarget = true; return true; }
            int count = Plan.NavigationNodes.Count, end = -1, bestApproach = -1;
            float bestDistance = Vector3.Distance(from, to), bestCost = 0f;
            heapCount = 0;
            for (int i = 0; i < count; i++)
            {
                routeCost[i] = float.PositiveInfinity; routePrevious[i] = -1; routeClosed[i] = false; heapIndex[i] = -1;
                routeEstimate[i] = Vector3.Distance(Plan.NavigationNodes[i],to);
            }
            for (int i = 0; i < count; i++)
                if ((from-Plan.NavigationNodes[i]).sqrMagnitude <= 9f &&
                    CanTraverse(from,Plan.NavigationNodes[i],radius,height,obstacleRadius))
                { routeCost[i] = Vector3.Distance(from,Plan.NavigationNodes[i]); PushOrDecrease(i); }
            while (heapCount > 0)
            {
                int current = PopNearest();
                if (supportedTarget && (Plan.NavigationNodes[current]-to).sqrMagnitude <= 9f &&
                    CanTraverse(Plan.NavigationNodes[current],to,radius,height,obstacleRadius))
                { end = current; reachesTarget = true; break; }
                if (allowPartial && (routeEstimate[current] < bestDistance - .0001f ||
                    Mathf.Abs(routeEstimate[current] - bestDistance) <= .0001f && routeCost[current] < bestCost))
                {
                    bestApproach = current;
                    bestDistance = routeEstimate[current]; bestCost = routeCost[current];
                }
                routeClosed[current] = true;
                foreach (Neighbour edge in neighbours[current])
                {
                    int neighbour = edge.Node;
                    if (routeClosed[neighbour]) continue;
                    if ((edge.GateSensitive || obstacleRadius > .3601f || height > DefaultHeadroom) &&
                        !CanTraverse(Plan.NavigationNodes[current],Plan.NavigationNodes[neighbour],radius,height,obstacleRadius)) continue;
                    float next = routeCost[current] + Vector3.Distance(Plan.NavigationNodes[current],Plan.NavigationNodes[neighbour]);
                    if (next >= routeCost[neighbour]) continue;
                    routeCost[neighbour] = next; routePrevious[neighbour] = current; PushOrDecrease(neighbour);
                }
            }
            if (end < 0)
            {
                if (!allowPartial) return false;
                end = bestApproach;
            }
            for (int i = end; i >= 0; i = routePrevious[i]) destination.Add(Plan.NavigationNodes[i]);
            destination.Reverse();
            if (reachesTarget) destination.Add(to);
            else
            {
                Vector3 last = end >= 0 ? Plan.NavigationNodes[end] : from;
                Vector3 approach = ExtendApproach(last, to, radius, height, obstacleRadius);
                if (destination.Count == 0 || (approach - last).sqrMagnitude > .000001f) destination.Add(approach);
            }
            Vector3 start = from;
            for (int i = 0; i + 1 < destination.Count;)
            {
                if (CanTraverse(start,destination[i+1],radius,height,obstacleRadius)) destination.RemoveAt(i);
                else { start = destination[i]; i++; }
            }
            return true;
        }

        private Vector3 ExtendApproach(Vector3 from, Vector3 target, float radius, float height, float obstacleRadius)
        {
            // At most three metres beyond the selected node. Each probe
            // retains supporting height and validates the entire segment, so a
            // gap, rail or a lower floor cannot become a partial-path shortcut.
            Vector3 delta = target - from; delta.y = 0f;
            delta = Vector3.ClampMagnitude(delta, 3f);
            if (delta.sqrMagnitude < .000001f) return from;
            float low = 0f, high = 1f;
            Vector3 result = from;
            for (int probe = 0; probe < 8; probe++)
            {
                float fraction = probe == 0 ? high : (low + high) * .5f;
                Vector3 candidate = from + delta * fraction;
                if (TryGetHeight(candidate, out float support, radius, height, obstacleRadius))
                {
                    candidate.y = support;
                    if (CanTraverse(from, candidate, radius, height, obstacleRadius))
                    { result = candidate; low = fraction; if (fraction == 1f) break; continue; }
                }
                high = fraction;
            }
            return (result - target).sqrMagnitude < (from - target).sqrMagnitude ? result : from;
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
