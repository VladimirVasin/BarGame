using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct StairwellEnemyPlacement
    {
        public readonly int Id, Variant;
        public readonly Vector3 Position, Facing;
        public StairwellEnemyPlacement(int id, Vector3 position, Vector3 facing)
        { Id = id; Variant = id is 11 or 15 or 18 ? 2 : id % 2; Position = position; Facing = facing; }
    }

    /// <summary>Authored placements reference the existing metric graph, never generated mesh coordinates.</summary>
    public sealed class StairwellEncounterPlan
    {
        public const float Floor = 12f, SafeRoomEdge = -5.5f;
        private static readonly Vector2[] CircleOffsets = {
            Vector2.right, Vector2.left, Vector2.up, Vector2.down,
            new Vector2(.7071068f, .7071068f), new Vector2(-.7071068f, .7071068f),
            new Vector2(.7071068f, -.7071068f), new Vector2(-.7071068f, -.7071068f)
        };
        public IReadOnlyList<StairwellEnemyPlacement> Enemies { get; }
        public IReadOnlyList<StairwellLabyrinthSupportPlan> Supports { get; }

        public StairwellEncounterPlan(StairwellLabyrinthPlan labyrinth)
        {
            if (labyrinth == null) throw new ArgumentNullException(nameof(labyrinth));
            var surfaces = new List<StairwellLabyrinthSupportPlan>();
            foreach (var surface in labyrinth.Supports)
                if (surface.Stage == 2 && !surface.IsRamp && !surface.Id.StartsWith("room-", StringComparison.Ordinal))
                    surfaces.Add(surface);
            Supports = surfaces.AsReadOnly();
            var placements = new List<StairwellEnemyPlacement>(StairwellEncounterState.Population);
            Place("overlook-blind-01", .85f, -.95f);
            Place("overlook-blind-01", .65f, .95f);
            Place("overlook-blind-01", .45f, -.95f);
            Place("overlook-blind-01", .20f, .95f);
            Place("overlook-blind-00", .78f, .95f);
            Place("overlook-blind-00", .30f, -.95f);
            Place("main-09", .82f, .95f);
            Place("main-09", .52f, -.95f);
            Place("main-09", .18f, .95f);
            Place("main-10", .27f, -.95f);
            Place("main-10", .60f, .95f);
            Place("main-10", .86f, -.95f);
            Place("main-11", .45f, .95f);
            Place("main-12", .38f, -.95f);
            Place("main-13", .30f, .95f);
            Place("main-13", .68f, -.95f);
            Place("service-return-00", .5f, .95f);
            Place("service-return-01", .33f, -.95f);
            Place("service-return-01", .78f, .95f);
            Place("service-return-02", .5f, -.95f);
            Place("duct-blind-01", .5f, -.95f);
            if (placements.Count != StairwellEncounterState.Population) throw new InvalidOperationException("Incomplete finite stairwell population.");
            Enemies = placements.AsReadOnly();

            void Place(string id, float along, float side)
            {
                foreach (var surface in Supports)
                {
                    if (surface.Id != id) continue;
                    Vector3 forward = (surface.End - surface.Start).normalized;
                    Vector3 lateral = Vector3.Cross(Vector3.up, forward);
                    Vector3 position = Vector3.Lerp(surface.Start, surface.End, along) + lateral * side;
                    position.y += PlayerFactory.GroundedRootOffset;
                    if (!Contains(position, .52f)) throw new InvalidOperationException("Unsupported encounter placement: " + id);
                    foreach (var previous in placements)
                        if (Vector3.Distance(previous.Position, position) < 1.7f)
                            throw new InvalidOperationException("Overlapping dormant bodies: " + id);
                    // Keep the long folded torsos along the landing, with the middle lane open.
                    placements.Add(new StairwellEnemyPlacement(placements.Count, position,
                        placements.Count % 2 == 0 ? forward : -forward));
                    return;
                }
                throw new InvalidOperationException("Missing encounter support: " + id);
            }
        }

        public bool Contains(Vector3 position, float radius = 0f)
        {
            if (FindSupport(position) < 0) return false;
            // Adjacent slabs share a traversable seam; eroding each rectangle would create a false gap.
            for (int i = 0; i < CircleOffsets.Length && radius > 0f; i++)
            {
                Vector2 offset = CircleOffsets[i] * radius;
                if (FindSupport(position + new Vector3(offset.x, 0f, offset.y)) < 0) return false;
            }
            return true;
        }
        internal int FindSupport(Vector3 p)
        {
            if (Mathf.Abs(p.y - Floor - PlayerFactory.GroundedRootOffset) > .6f || p.x < SafeRoomEdge) return -1;
            for (int i = 0; i < Supports.Count; i++)
            {
                Rect bounds = Supports[i].Bounds;
                if (p.x >= bounds.xMin && p.x <= bounds.xMax && p.z >= bounds.yMin && p.z <= bounds.yMax) return i;
            }
            return -1;
        }
    }
}
