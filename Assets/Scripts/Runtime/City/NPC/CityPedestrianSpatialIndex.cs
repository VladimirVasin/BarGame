using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Reused planar broad phase for street personal space. Entries are updated
    /// after each walker moves, so later walkers see the same positions as the
    /// old ordered peer scan, including on a long frame. Candidate order stays
    /// tied to the actor slot rather than dictionary or cell iteration order.
    /// </summary>
    public sealed class CityPedestrianSpatialIndex
    {
        public const float CellSize = 2f;

        private Dictionary<Vector2Int, int> heads =
            new Dictionary<Vector2Int, int>();
        private Vector2Int[] cells = Array.Empty<Vector2Int>();
        private Vector3[] positions = Array.Empty<Vector3>();
        private int[] next = Array.Empty<int>();
        private int[] previous = Array.Empty<int>();
        private bool[] included = Array.Empty<bool>();
        private int[] candidates = Array.Empty<int>();
        private int slotCount;
        private int candidateCount;

        /// <summary>Starts one frame's broad phase; retained storage is bounded
        /// by the largest configured actor pool, never by world travel.</summary>
        public void BeginRebuild(int actorSlotCount)
        {
            if (actorSlotCount < 0)
                throw new ArgumentOutOfRangeException(nameof(actorSlotCount));

            if (cells.Length < actorSlotCount)
            {
                cells = new Vector2Int[actorSlotCount];
                positions = new Vector3[actorSlotCount];
                next = new int[actorSlotCount];
                previous = new int[actorSlotCount];
                included = new bool[actorSlotCount];
                candidates = new int[actorSlotCount];
                heads = new Dictionary<Vector2Int, int>(actorSlotCount);
            }
            else
            {
                heads.Clear();
                Array.Clear(included, 0, included.Length);
            }

            slotCount = actorSlotCount;
            candidateCount = 0;
        }

        public void Rebuild(IReadOnlyList<CityPedestrianActor> actors)
        {
            if (actors == null) throw new ArgumentNullException(nameof(actors));
            BeginRebuild(actors.Count);
            for (int index = 0; index < actors.Count; index++)
                Update(index, actors[index]);
        }

        public void Update(int actorIndex, CityPedestrianActor actor)
        {
            bool present = actor != null && actor.IsSpawned &&
                !actor.IsAttachedToVehicle;
            Update(actorIndex, present ? actor.Position : Vector3.zero, present);
        }

        /// <summary>Updates or removes one slot without allocating. Explicit
        /// position input also allows the broad phase to be verified without
        /// instantiating character meshes or their animation graphs.</summary>
        public void Update(int actorIndex, Vector3 position, bool present = true)
        {
            if (actorIndex < 0 || actorIndex >= slotCount)
                throw new ArgumentOutOfRangeException(nameof(actorIndex));

            if (!present)
            {
                Remove(actorIndex);
                return;
            }

            if (!IsFinite(position.x) || !IsFinite(position.z))
                throw new ArgumentOutOfRangeException(nameof(position));

            Vector2Int cell = CellFor(position);
            positions[actorIndex] = position;
            if (included[actorIndex] && cells[actorIndex] == cell) return;
            Remove(actorIndex);
            cells[actorIndex] = cell;
            previous[actorIndex] = -1;
            next[actorIndex] = heads.TryGetValue(cell, out int head) ? head : -1;
            if (next[actorIndex] >= 0) previous[next[actorIndex]] = actorIndex;
            heads[cell] = actorIndex;
            included[actorIndex] = true;
        }

        /// <summary>Returns every present slot within the planar radius,
        /// ordered by stable actor index. Results remain valid until the next
        /// query; the exact facing/avoidance test belongs to the director.</summary>
        public int Query(Vector3 position, float radius)
        {
            // Avoidance is local. This also bounds the lookup to at most nine
            // cells even if a caller accidentally supplies a world radius.
            if (!IsFinite(position.x) || !IsFinite(position.z) ||
                !IsFinite(radius) || radius < 0f || radius > CellSize)
                throw new ArgumentOutOfRangeException(nameof(radius));

            candidateCount = 0;
            Vector2Int minimum = CellFor(position - new Vector3(radius, 0f, radius));
            Vector2Int maximum = CellFor(position + new Vector3(radius, 0f, radius));
            float squaredRadius = radius * radius;
            for (int x = minimum.x; x <= maximum.x; x++)
            {
                for (int z = minimum.y; z <= maximum.y; z++)
                {
                    if (!heads.TryGetValue(new Vector2Int(x, z), out int index))
                        continue;
                    while (index >= 0)
                    {
                        Vector3 offset = positions[index] - position;
                        if (offset.x * offset.x + offset.z * offset.z <= squaredRadius)
                            candidates[candidateCount++] = index;
                        index = next[index];
                    }
                }
            }

            Array.Sort(candidates, 0, candidateCount);
            return candidateCount;
        }

        public int GetActorIndex(int resultIndex)
        {
            if (resultIndex < 0 || resultIndex >= candidateCount)
                throw new ArgumentOutOfRangeException(nameof(resultIndex));
            return candidates[resultIndex];
        }

        private void Remove(int index)
        {
            if (!included[index]) return;
            int before = previous[index];
            int after = next[index];
            if (before >= 0) next[before] = after;
            else if (after >= 0) heads[cells[index]] = after;
            else heads.Remove(cells[index]);
            if (after >= 0) previous[after] = before;
            included[index] = false;
        }

        private static Vector2Int CellFor(Vector3 position) => new Vector2Int(
            Mathf.FloorToInt(position.x / CellSize),
            Mathf.FloorToInt(position.z / CellSize));

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
