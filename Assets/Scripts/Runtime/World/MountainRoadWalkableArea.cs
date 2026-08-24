using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A capsule-chain traversal mask over the real curved centreline. Unlike
    /// a union of broad rectangles, it leaves both hairpin holes closed and
    /// cannot join neighbouring switchback shelves across the rock between.
    /// </summary>
    public sealed class MountainRoadWalkableArea : IWalkableArea
    {
        private const float Epsilon = 0.0001f;

        private readonly MountainRoadPlan plan;

        public MountainRoadWalkableArea(MountainRoadPlan plan)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            MountainRoadValidator.ValidateOrThrow(plan);
        }

        public MountainRoadPlan Plan => plan;

        public bool Contains(Vector3 position, float radius = 0f)
        {
            ValidateRadius(radius);
            Vector2 point = ToXZ(position);
            return ContainsTunnel(point, radius) ||
                   ContainsRoute(point, radius) ||
                   ContainsPlateau(point, radius);
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

            TryClosestTunnel(point, radius, ref found, ref bestSqr, ref best);
            TryClosestRoute(point, radius, ref found, ref bestSqr, ref best);
            TryClosestPlateau(point, radius, ref found, ref bestSqr, ref best);
            if (!found)
            {
                return plan.SpawnPosition;
            }

            return new Vector3(best.x, position.y, best.y);
        }

        private bool ContainsTunnel(Vector2 point, float radius)
        {
            MountainRoadTunnelDescriptor tunnel = plan.Tunnel;
            Vector2 portal = ToXZ(tunnel.PortalGroundCenter);
            Vector2 axis = ToXZ(tunnel.OutwardAxis).normalized;
            Vector2 right = new Vector2(axis.y, -axis.x);
            Vector2 delta = point - portal;
            float along = Vector2.Dot(delta, axis);
            float lateral = Mathf.Abs(Vector2.Dot(delta, right));
            return along >= -tunnel.VisualDepth + radius - Epsilon &&
                   along <= radius + Epsilon &&
                   lateral <= tunnel.OpeningWidth * 0.5f - radius + Epsilon;
        }

        private bool ContainsRoute(Vector2 point, float radius)
        {
            IReadOnlyList<MountainRoadRouteSample> samples = plan.Route.Samples;
            for (int index = 1; index < samples.Count; index++)
            {
                MountainRoadRouteSample first = samples[index - 1];
                MountainRoadRouteSample second = samples[index];
                Vector2 a = ToXZ(first.Position);
                Vector2 b = ToXZ(second.Position);
                Vector2 ab = b - a;
                float denominator = ab.sqrMagnitude;
                float t = denominator <= Epsilon
                    ? 0f
                    : Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
                float effectiveHalfWidth = Mathf.Lerp(
                    first.Width,
                    second.Width,
                    t) * 0.5f - radius;
                if (effectiveHalfWidth >= 0f &&
                    Vector2.Distance(point, Vector2.Lerp(a, b, t)) <=
                    effectiveHalfWidth + Epsilon)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ContainsPlateau(Vector2 point, float radius)
        {
            if (!plan.Plateau.Contains(point))
            {
                return false;
            }

            if (radius <= Epsilon)
            {
                return true;
            }

            return DistanceToPlateauEdge(point) + Epsilon >= radius;
        }

        private void TryClosestTunnel(
            Vector2 point,
            float radius,
            ref bool found,
            ref float bestSqr,
            ref Vector2 best)
        {
            MountainRoadTunnelDescriptor tunnel = plan.Tunnel;
            float halfWidth = tunnel.OpeningWidth * 0.5f - radius;
            float depth = tunnel.VisualDepth - radius;
            if (halfWidth < 0f || depth < 0f)
            {
                return;
            }

            Vector2 portal = ToXZ(tunnel.PortalGroundCenter);
            Vector2 axis = ToXZ(tunnel.OutwardAxis).normalized;
            Vector2 right = new Vector2(axis.y, -axis.x);
            Vector2 delta = point - portal;
            float along = Mathf.Clamp(Vector2.Dot(delta, axis), -depth, 0f);
            float lateral = Mathf.Clamp(
                Vector2.Dot(delta, right),
                -halfWidth,
                halfWidth);
            Consider(
                point,
                portal + axis * along + right * lateral,
                ref found,
                ref bestSqr,
                ref best);
        }

        private void TryClosestRoute(
            Vector2 point,
            float radius,
            ref bool found,
            ref float bestSqr,
            ref Vector2 best)
        {
            IReadOnlyList<MountainRoadRouteSample> samples = plan.Route.Samples;
            for (int index = 1; index < samples.Count; index++)
            {
                MountainRoadRouteSample first = samples[index - 1];
                MountainRoadRouteSample second = samples[index];
                Vector2 a = ToXZ(first.Position);
                Vector2 b = ToXZ(second.Position);
                Vector2 ab = b - a;
                float denominator = ab.sqrMagnitude;
                float t = denominator <= Epsilon
                    ? 0f
                    : Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
                Vector2 center = Vector2.Lerp(a, b, t);
                Vector2 direction = denominator <= Epsilon
                    ? ToXZ(first.Forward)
                    : ab.normalized;
                Vector2 right = new Vector2(direction.y, -direction.x);
                float halfWidth = Mathf.Lerp(
                    first.Width,
                    second.Width,
                    t) * 0.5f - radius;
                if (halfWidth < 0f)
                {
                    continue;
                }

                float lateral = Mathf.Clamp(
                    Vector2.Dot(point - center, right),
                    -halfWidth,
                    halfWidth);
                Consider(
                    point,
                    center + right * lateral,
                    ref found,
                    ref bestSqr,
                    ref best);
            }
        }

        private void TryClosestPlateau(
            Vector2 point,
            float radius,
            ref bool found,
            ref float bestSqr,
            ref Vector2 best)
        {
            IReadOnlyList<Vector2> vertices = plan.Plateau.VerticesXZ;
            Vector2 center = ToXZ(plan.Plateau.Center);
            if (ContainsPlateau(point, radius))
            {
                Consider(
                    point,
                    point,
                    ref found,
                    ref bestSqr,
                    ref best);
                return;
            }

            for (int index = 0; index < vertices.Count; index++)
            {
                Vector2 a = vertices[index];
                Vector2 b = vertices[(index + 1) % vertices.Count];
                Vector2 edge = b - a;
                float denominator = edge.sqrMagnitude;
                float t = denominator <= Epsilon
                    ? 0f
                    : Mathf.Clamp01(Vector2.Dot(point - a, edge) / denominator);
                Vector2 edgePoint = Vector2.Lerp(a, b, t);
                Vector2 inward = center - edgePoint;
                if (inward.sqrMagnitude > Epsilon)
                {
                    edgePoint += inward.normalized * radius;
                }

                Consider(
                    point,
                    edgePoint,
                    ref found,
                    ref bestSqr,
                    ref best);
            }
        }

        private float DistanceToPlateauEdge(Vector2 point)
        {
            float best = float.PositiveInfinity;
            IReadOnlyList<Vector2> vertices = plan.Plateau.VerticesXZ;
            for (int index = 0; index < vertices.Count; index++)
            {
                Vector2 a = vertices[index];
                Vector2 b = vertices[(index + 1) % vertices.Count];
                Vector2 ab = b - a;
                float denominator = ab.sqrMagnitude;
                float t = denominator <= Epsilon
                    ? 0f
                    : Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
                best = Mathf.Min(
                    best,
                    Vector2.Distance(point, Vector2.Lerp(a, b, t)));
            }

            return best;
        }

        private static void Consider(
            Vector2 source,
            Vector2 candidate,
            ref bool found,
            ref float bestSqr,
            ref Vector2 best)
        {
            float sqr = (candidate - source).sqrMagnitude;
            if (!found || sqr < bestSqr)
            {
                found = true;
                bestSqr = sqr;
                best = candidate;
            }
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        private static void ValidateRadius(float radius)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0f)
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
    }
}
