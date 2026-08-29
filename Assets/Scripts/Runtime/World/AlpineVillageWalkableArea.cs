using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A capsule-chain traversal mask over the real lane centreline, plus one
    /// oriented rectangle per level shelf.
    ///
    /// Deliberately not a union of axis-aligned rectangles like the City's:
    /// there, a point with a non-zero radius has to fit inside ONE rectangle,
    /// abutting rectangles do not merge, and every junction needs an explicit
    /// seam strip. A chain of capsules along the road that is actually there
    /// has no seams to forget.
    /// </summary>
    public sealed class AlpineVillageWalkableArea : IWalkableArea
    {
        /// <summary>
        /// Walkable ground either side of the carriageway. Wide enough that
        /// the hero can stand out of the middle of the street and reach a
        /// door, narrow enough that he cannot wander onto the slope.
        /// </summary>
        public const float LaneShoulder = 0.9f;

        /// <summary>Half-width of a branch out to a spur.</summary>
        public const float SpurHalfWidth =
            AlpineVillagePathPlanner.BranchWalkableHalfWidth;

        /// <summary>Depth of the level apron kept in front of a threshold.
        /// </summary>
        public const float DoorApronDepth = 2.2f;

        private readonly AlpineVillagePlan plan;
        private readonly List<Capsule> capsules = new List<Capsule>();
        private readonly List<OrientedRect> rects = new List<OrientedRect>();

        public AlpineVillageWalkableArea(AlpineVillagePlan plan)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            AlpineVillageValidator.ValidateOrThrow(plan);
            BuildLane();
            BuildStation();
            BuildPaths();
            BuildPlots();
        }

        public AlpineVillagePlan Plan => plan;

        public bool Contains(Vector3 position, float radius = 0f)
        {
            ValidateRadius(radius);
            Vector2 point = ToXZ(position);
            for (int index = 0; index < capsules.Count; index++)
            {
                if (capsules[index].Contains(point, radius))
                {
                    return true;
                }
            }

            for (int index = 0; index < rects.Count; index++)
            {
                if (rects[index].Contains(point, radius))
                {
                    return true;
                }
            }

            return false;
        }

        public Vector3 Constrain(
            Vector3 currentPosition,
            Vector3 desiredPosition,
            float radius = 0f)
        {
            ValidateRadius(radius);
            if (Contains(desiredPosition, radius))
            {
                return desiredPosition;
            }

            if (!IsFinite(desiredPosition))
            {
                return currentPosition;
            }

            Vector3 closest = ClosestPoint(desiredPosition, radius);
            if (Contains(closest, Mathf.Max(0f, radius - 0.001f)))
            {
                return closest;
            }

            // Sliding failed, so keep him where he was rather than teleporting
            // him across the village - the mountain road's fallback.
            return Contains(currentPosition, radius)
                ? new Vector3(
                    currentPosition.x,
                    desiredPosition.y,
                    currentPosition.z)
                : plan.SpawnPosition;
        }

        public Vector3 ClosestPoint(Vector3 position, float radius = 0f)
        {
            ValidateRadius(radius);
            Vector2 point = ToXZ(position);
            bool found = false;
            float bestSqr = float.PositiveInfinity;
            Vector2 best = default;

            for (int index = 0; index < capsules.Count; index++)
            {
                Vector2 candidate = capsules[index].ClosestPoint(
                    point,
                    radius);
                Consider(point, candidate, ref found, ref bestSqr, ref best);
            }

            for (int index = 0; index < rects.Count; index++)
            {
                Vector2 candidate = rects[index].ClosestPoint(point, radius);
                Consider(point, candidate, ref found, ref bestSqr, ref best);
            }

            if (!found)
            {
                return plan.SpawnPosition;
            }

            return new Vector3(best.x, position.y, best.y);
        }

        private static void Consider(
            Vector2 point,
            Vector2 candidate,
            ref bool found,
            ref float bestSqr,
            ref Vector2 best)
        {
            float distance = (candidate - point).sqrMagnitude;
            if (found && distance >= bestSqr)
            {
                return;
            }

            found = true;
            bestSqr = distance;
            best = candidate;
        }

        private void BuildLane()
        {
            IReadOnlyList<AlpineVillageLaneSample> samples = plan.Lane.Samples;
            for (int index = 0; index < samples.Count - 1; index++)
            {
                AlpineVillageLaneSample first = samples[index];
                AlpineVillageLaneSample second = samples[index + 1];
                capsules.Add(new Capsule(
                    ToXZ(first.Position),
                    ToXZ(second.Position),
                    Mathf.Max(first.Width, second.Width) * 0.5f +
                    LaneShoulder));
            }
        }

        private void BuildStation()
        {
            AlpineVillageStationPlan station = plan.Station;
            MountainRoadTerminalRect pad = station.PadArea;
            rects.Add(new OrientedRect(
                ToXZ(pad.Center),
                ToXZ(pad.Right),
                ToXZ(pad.Forward),
                pad.Size * 0.5f));

            // The boarding strip runs off the FRONT of the pad and stands on
            // its own apron, so the mask has to follow it out there. Without
            // this the far half metre of the platform - and the whole apron -
            // is a wall the hero walks into while standing on concrete.
            MountainRoadTerminalRect apron =
                station.Cableway.BoardingApronArea;
            rects.Add(new OrientedRect(
                ToXZ(apron.Center),
                ToXZ(apron.Right),
                ToXZ(apron.Forward),
                apron.Size * 0.5f));

            // The pad sits behind the lane foot; join the two so the step off
            // the platform is not a hole in the mask.
            capsules.Add(new Capsule(
                ToXZ(pad.Center),
                ToXZ(plan.Lane.Start),
                2.2f));
            capsules.Add(new Capsule(
                ToXZ(pad.Center),
                ToXZ(station.BoardingDockPosition),
                1.6f));

            // The route from the foot of the steps to the lane is part of the
            // visible path plan below. It used to be a second, invisible
            // capsule authored independently of the ground that depicts it.
        }

        /// <summary>
        /// Every permitted branch is also a visible compacted track. Both
        /// systems consume these exact endpoints and widths, so navigation
        /// cannot silently grow a shortcut across untouched snow.
        /// </summary>
        private void BuildPaths()
        {
            IReadOnlyList<AlpineVillagePathDescriptor> paths =
                AlpineVillagePathPlanner.Create(plan);
            for (int index = 0; index < paths.Count; index++)
            {
                AlpineVillagePathDescriptor path = paths[index];
                capsules.Add(new Capsule(
                    ToXZ(path.Start),
                    ToXZ(path.End),
                    path.WalkableHalfWidth));
            }
        }

        /// <summary>
        /// Every plot gets an apron in front of its door. Its route back to
        /// the lane belongs to <see cref="BuildPaths"/>.
        /// </summary>
        private void BuildPlots()
        {
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                Vector2 facing = ToXZ(plot.Facing).normalized;
                Vector2 across = new Vector2(facing.y, -facing.x);
                Vector2 apronCenter = ToXZ(plot.DoorGroundPosition) +
                                      facing * (DoorApronDepth * 0.5f);
                rects.Add(new OrientedRect(
                    apronCenter,
                    across,
                    facing,
                    new Vector2(
                        Mathf.Max(2.4f, plot.FootprintSize.x * 0.5f),
                        DoorApronDepth * 0.5f)));

            }
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        private static void ValidateRadius(float radius)
        {
            if (radius < 0f || float.IsNaN(radius) || float.IsInfinity(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private readonly struct Capsule
        {
            private readonly Vector2 start;
            private readonly Vector2 end;
            private readonly float halfWidth;

            internal Capsule(Vector2 start, Vector2 end, float halfWidth)
            {
                this.start = start;
                this.end = end;
                this.halfWidth = halfWidth;
            }

            internal bool Contains(Vector2 point, float radius)
            {
                float allowed = halfWidth - radius;
                if (allowed <= 0f)
                {
                    return false;
                }

                return (point - Project(point)).sqrMagnitude <=
                       allowed * allowed;
            }

            internal Vector2 ClosestPoint(Vector2 point, float radius)
            {
                float allowed = Mathf.Max(0f, halfWidth - radius);
                Vector2 axisPoint = Project(point);
                Vector2 offset = point - axisPoint;
                float distance = offset.magnitude;
                if (distance <= allowed || distance <= 0.000001f)
                {
                    return point;
                }

                return axisPoint + offset / distance * allowed;
            }

            private Vector2 Project(Vector2 point)
            {
                Vector2 segment = end - start;
                float lengthSquared = segment.sqrMagnitude;
                if (lengthSquared <= 0.000001f)
                {
                    return start;
                }

                float amount = Mathf.Clamp01(
                    Vector2.Dot(point - start, segment) / lengthSquared);
                return start + segment * amount;
            }
        }

        private readonly struct OrientedRect
        {
            private readonly Vector2 center;
            private readonly Vector2 axisX;
            private readonly Vector2 axisY;
            private readonly Vector2 halfSize;

            internal OrientedRect(
                Vector2 center,
                Vector2 axisX,
                Vector2 axisY,
                Vector2 halfSize)
            {
                this.center = center;
                this.axisX = axisX.normalized;
                this.axisY = axisY.normalized;
                this.halfSize = halfSize;
            }

            internal bool Contains(Vector2 point, float radius)
            {
                Vector2 local = ToLocal(point);
                return Mathf.Abs(local.x) <= halfSize.x - radius &&
                       Mathf.Abs(local.y) <= halfSize.y - radius;
            }

            internal Vector2 ClosestPoint(Vector2 point, float radius)
            {
                float allowedX = Mathf.Max(0f, halfSize.x - radius);
                float allowedY = Mathf.Max(0f, halfSize.y - radius);
                Vector2 local = ToLocal(point);
                local.x = Mathf.Clamp(local.x, -allowedX, allowedX);
                local.y = Mathf.Clamp(local.y, -allowedY, allowedY);
                return center + axisX * local.x + axisY * local.y;
            }

            private Vector2 ToLocal(Vector2 point)
            {
                Vector2 delta = point - center;
                return new Vector2(
                    Vector2.Dot(delta, axisX),
                    Vector2.Dot(delta, axisY));
            }
        }
    }
}
