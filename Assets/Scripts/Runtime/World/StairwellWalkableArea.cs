using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Finite support surfaces for the post-sleep stairwell. Storeys sharing XZ
    /// never become one floor; physics still owns the capsule's vertical motion.
    /// </summary>
    public sealed class StairwellWalkableArea : IWalkableArea
    {
        private const float CellSize = 8f;
        private const float SurfaceCapture = .7f;
        private const float MaximumStep = .28f;
        private const float StandingHeight = 1.85f;
        private const float Epsilon = .002f;
        private static readonly Vector2[] CircleOffsets = CreateCircleOffsets();
        private readonly Transform space;
        private readonly StairwellLabyrinthPuzzleState puzzles;
        private readonly StairwellLabyrinthGatePlan[] gates;
        private readonly Support[] supports;
        private readonly Dictionary<Vector2Int, List<int>> cells = new Dictionary<Vector2Int, List<int>>();

        private readonly struct Support
        {
            public readonly Rect Bounds;
            public readonly Vector3 Start, End;
            public Support(Rect bounds, Vector3 start, Vector3 end)
            { Bounds = bounds; Start = start; End = end; }
            public float HeightAt(Vector3 position)
            {
                Vector2 delta = new Vector2(End.x - Start.x, End.z - Start.z);
                float length = delta.sqrMagnitude;
                float t = length < .0001f ? 0f : Mathf.Clamp01(
                    Vector2.Dot(new Vector2(position.x - Start.x, position.z - Start.z), delta) / length);
                return Mathf.Lerp(Start.y, End.y, t);
            }
        }

        public StairwellWalkableArea(StairwellLayoutPlan plan, Transform space = null, StairwellLabyrinthPuzzleState puzzles = null)
        {
            if (plan == null || plan.Labyrinth == null)
                throw new ArgumentException("Height-aware traversal requires the post-sleep stairwell.", nameof(plan));
            this.space = space;
            this.puzzles = puzzles;
            gates = puzzles != null ? StairwellLabyrinthData.CreatePuzzleGates() : Array.Empty<StairwellLabyrinthGatePlan>();
            supports = new Support[plan.Labyrinth.Supports.Count + 3];
            supports[0] = Flat(plan.ApartmentLandingBounds, plan.ApartmentElevation);
            StairwellFlightPlan flight = plan.UpperFlight;
            Vector2 end = flight.Start + flight.Direction * flight.RunLength;
            supports[1] = new Support(plan.UpperFlightBounds,
                new Vector3(flight.Start.x, flight.BaseElevation, flight.Start.y),
                new Vector3(end.x, flight.TopElevation, end.y));
            supports[2] = Flat(plan.UpperLandingBounds, flight.TopElevation);
            for (int i = 0; i < plan.Labyrinth.Supports.Count; i++)
            {
                StairwellLabyrinthSupportPlan surface = plan.Labyrinth.Supports[i];
                supports[i + 3] = new Support(surface.Bounds, surface.Start, surface.End);
            }
            for (int i = 0; i < supports.Length; i++)
            {
                Rect bounds = supports[i].Bounds;
                for (int z = Mathf.FloorToInt(bounds.yMin / CellSize); z <= Mathf.FloorToInt(bounds.yMax / CellSize); z++)
                for (int x = Mathf.FloorToInt(bounds.xMin / CellSize); x <= Mathf.FloorToInt(bounds.xMax / CellSize); x++)
                {
                    var key = new Vector2Int(x, z);
                    if (!cells.TryGetValue(key, out List<int> indices))
                    { indices = new List<int>(8); cells.Add(key, indices); }
                    indices.Add(i);
                }
            }
        }

        public bool Contains(Vector3 position, float radius = 0f) => TryGetHeight(position, out _, radius);

        public bool TryGetHeight(Vector3 position, out float height, float radius = 0f)
        {
            Vector3 local = ToLocal(position);
            bool found = TrySupport(local, radius, SurfaceCapture, SurfaceCapture, out float floor);
            height = found && space != null ? space.TransformPoint(new Vector3(local.x, floor, local.z)).y : floor;
            return found;
        }

        public Vector3 Constrain(Vector3 currentPosition, Vector3 desiredPosition, float radius = 0f)
        {
            if (!Finite(currentPosition) || !Finite(desiredPosition) || !Finite(radius) || radius < 0f)
                return currentPosition;
            Vector3 current = ToLocal(currentPosition), desired = ToLocal(desiredPosition);
            if (StepAllowed(current, desired, radius)) return desiredPosition;
            Vector3 alongX = new Vector3(desired.x, desired.y, current.z);
            Vector3 alongZ = new Vector3(current.x, desired.y, desired.z);
            bool xAllowed = StepAllowed(current, alongX, radius);
            bool zAllowed = StepAllowed(current, alongZ, radius);
            if (xAllowed && zAllowed)
                return ToWorld((alongX - desired).sqrMagnitude <= (alongZ - desired).sqrMagnitude ? alongX : alongZ);
            if (xAllowed) return ToWorld(alongX);
            if (zAllowed) return ToWorld(alongZ);
            return ToWorld(new Vector3(current.x, desired.y, current.z));
        }

        private bool StepAllowed(Vector3 current, Vector3 desired, float radius)
        {
            // A jump may rise above the support-capture band. Follow the same
            // connected floor below it, without promoting the capsule to a roof.
            if (!TrySupport(current, radius, .32f, 2.25f, out float before)) return false;
            float distance = new Vector2(desired.x - current.x, desired.z - current.z).magnitude;
            int samples = Mathf.Max(1, Mathf.CeilToInt(distance / .15f));
            if (samples > 64) return false;
            Vector3 previous = current;
            for (int i = 1; i <= samples; i++)
            {
                Vector3 sample = Vector3.Lerp(current, desired, i / (float)samples);
                // A connected ramp can change height; an overlapping storey
                // cannot claim support merely because it shares a footprint.
                sample.y = before + PlayerFactory.GroundedRootOffset;
                if (CrossesClosedGate(previous, sample, before, radius)) return false;
                if (!TrySupport(sample, radius, MaximumStep + .05f, MaximumStep + .05f, out float after) ||
                    Mathf.Abs(after - before) > MaximumStep + Epsilon) return false;
                before = after;
                previous = sample;
            }
            return true;
        }

        private bool TrySupport(Vector3 point, float radius, float above, float below, out float floor)
        {
            floor = 0f;
            if (!Finite(point) || !Finite(radius) || radius < 0f ||
                !cells.TryGetValue(Cell(point.x, point.z), out List<int> indices)) return false;
            float best = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < indices.Count; i++)
            {
                Support candidate = supports[indices[i]];
                if (!Inside(candidate.Bounds, point.x, point.z)) continue;
                float value = candidate.HeightAt(point);
                float delta = value - point.y;
                if (delta > above || delta < -below || Mathf.Abs(delta) >= best ||
                    !HasHeadroom(point, value) || !CircleSupported(point, value, radius)) continue;
                floor = value; best = Mathf.Abs(delta); found = true;
            }
            return found;
        }

        private bool CircleSupported(Vector3 point, float floor, float radius)
        {
            // Test the union, rather than eroding every landing separately:
            // neighbouring slabs and ramps share a real traversable seam.
            for (int i = 0; i < CircleOffsets.Length && radius > 0f; i++)
            {
                Vector2 offset = CircleOffsets[i] * radius;
                var edge = new Vector3(point.x + offset.x, floor, point.z + offset.y);
                if (!cells.TryGetValue(Cell(edge.x, edge.z), out List<int> indices)) return false;
                bool found = false;
                for (int j = 0; j < indices.Count; j++)
                {
                    Support surface = supports[indices[j]];
                    if (Inside(surface.Bounds, edge.x, edge.z) &&
                        Mathf.Abs(surface.HeightAt(edge) - floor) <= radius * .6f + .03f)
                    { found = true; break; }
                }
                if (!found) return false;
            }
            return true;
        }

        private bool HasHeadroom(Vector3 point, float floor)
        {
            if (!cells.TryGetValue(Cell(point.x, point.z), out List<int> indices)) return false;
            for (int i = 0; i < indices.Count; i++)
            {
                Support surface = supports[indices[i]];
                if (!Inside(surface.Bounds, point.x, point.z)) continue;
                float underside = surface.HeightAt(point) - .24f;
                if (underside > floor + .28f && underside < floor + StandingHeight) return false;
            }
            return true;
        }

        private bool CrossesClosedGate(Vector3 current, Vector3 desired, float supportHeight, float radius)
        {
            foreach (StairwellLabyrinthGatePlan gate in gates)
            {
                if (puzzles.CanPassGate(gate.Stage) || Mathf.Abs(supportHeight - gate.Position.y) > 1f) continue;
                // The generator owns the very same ramp cut and physical leaf width.
                // Floor height keeps this guard authoritative throughout an ordinary jump.
                float from = Vector3.Dot(current - gate.Position, gate.Normal);
                float to = Vector3.Dot(desired - gate.Position, gate.Normal);
                float clearance = radius + .09f;
                bool crossing = from <= 0f ? to > from && to > -clearance : to < from && to < clearance;
                if (!crossing) continue;
                float boundary = from <= 0f ? -clearance : clearance;
                float t = Mathf.Abs(to - from) > .0001f ? Mathf.Clamp01((boundary - from) / (to - from)) : 0f;
                Vector3 point = Vector3.Lerp(current, desired, t);
                Vector3 across = Vector3.Cross(Vector3.up, gate.Normal);
                if (Mathf.Abs(Vector3.Dot(point - gate.Position, across)) <= gate.Width * .5f + radius) return true;
            }
            return false;
        }

        private static Vector2[] CreateCircleOffsets()
        {
            var offsets = new Vector2[12];
            for (int i = 0; i < offsets.Length; i++)
            {
                float angle = i * (Mathf.PI / 6f);
                offsets[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            return offsets;
        }

        private static Support Flat(Rect bounds, float height) => new Support(bounds,
            new Vector3(bounds.center.x, height, bounds.center.y), new Vector3(bounds.center.x, height, bounds.center.y));
        private static bool Inside(Rect bounds, float x, float z) => x >= bounds.xMin - Epsilon &&
            x <= bounds.xMax + Epsilon && z >= bounds.yMin - Epsilon && z <= bounds.yMax + Epsilon;
        private static Vector2Int Cell(float x, float z) => new Vector2Int(Mathf.FloorToInt(x / CellSize), Mathf.FloorToInt(z / CellSize));
        private Vector3 ToLocal(Vector3 point) => space != null ? space.InverseTransformPoint(point) : point;
        private Vector3 ToWorld(Vector3 point) => space != null ? space.TransformPoint(point) : point;
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
