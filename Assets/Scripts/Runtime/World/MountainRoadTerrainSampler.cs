using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One pure terrain-height contract shared by planning, validation and
    /// mesh construction. The soil is deliberately sunk below the road skin;
    /// the asphalt never relies on a coplanar terrain cutout.
    /// </summary>
    internal static class MountainRoadTerrainSampler
    {
        internal const float RoadBedClearance = 0.24f;
        internal const float PlateauExteriorBlendDistance = 12f;

        internal static float SampleHeight(
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau,
            Vector2 point)
        {
            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            if (plateau == null)
            {
                throw new ArgumentNullException(nameof(plateau));
            }

            if (plateau.Contains(point))
            {
                return plateau.Center.y - RoadBedClearance;
            }

            MountainRoadRouteSample start = route.Samples[0];
            Vector2 startXZ = new Vector2(
                start.Position.x,
                start.Position.z);
            Vector2 tunnelAxis = new Vector2(
                start.Forward.x,
                start.Forward.z).normalized;
            Vector2 tunnelRight = new Vector2(
                tunnelAxis.y,
                -tunnelAxis.x);
            Vector2 tunnelDelta = point - startXZ;
            float tunnelProgress = Vector2.Dot(tunnelDelta, tunnelAxis);
            float tunnelLateral = Mathf.Abs(Vector2.Dot(
                tunnelDelta,
                tunnelRight));
            if (tunnelProgress <= 0f &&
                tunnelProgress >= -MountainRoadPlanner.TunnelVisualDepth &&
                tunnelLateral <=
                CityMountainBoundaryDefinition.TunnelOpeningWidth * 0.5f)
            {
                return start.Position.y - RoadBedClearance;
            }

            FindClosest(
                route,
                point,
                out float distance,
                out Vector3 center,
                out Vector3 right,
                out float halfWidth);
            Vector2 delta = point - new Vector2(center.x, center.z);
            float signedLateral = Vector2.Dot(
                delta,
                new Vector2(right.x, right.z));
            float shoulderDistance = Mathf.Max(0f, distance - halfWidth);
            float worldSide = Mathf.Sign(signedLateral) * right.x;
            float bankSlope = worldSide >= 0f ? 0.22f : -0.18f;
            float roadBank = center.y - RoadBedClearance +
                             shoulderDistance * bankSlope;

            float macro = point.x * 0.30f + point.y * 0.035f - 0.55f;
            float undulation = Mathf.Sin(point.x * 0.31f + point.y * 0.17f) *
                               0.20f +
                               Mathf.Sin(point.x * -0.11f + point.y * 0.27f) *
                               0.12f;
            float blend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(7f, 14f, distance));
            float terrain = Mathf.Lerp(
                roadBank,
                macro + undulation,
                blend);
            float plateauDistance = DistanceToPolygonEdge(
                plateau.VerticesXZ,
                point);
            if (plateauDistance >= PlateauExteriorBlendDistance)
            {
                return terrain;
            }

            float exteriorBlend = Mathf.SmoothStep(
                0f,
                1f,
                plateauDistance / PlateauExteriorBlendDistance);
            float plateauBlended = Mathf.Lerp(
                plateau.Center.y - RoadBedClearance,
                terrain,
                exteriorBlend);
            float shoulderBlend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    halfWidth + 0.3f,
                    halfWidth + 2f,
                    distance));
            return Mathf.Lerp(terrain, plateauBlended, shoulderBlend);
        }

        private static float DistanceToPolygonEdge(
            System.Collections.Generic.IReadOnlyList<Vector2> polygon,
            Vector2 point)
        {
            float bestSquared = float.PositiveInfinity;
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 first = polygon[index];
                Vector2 second = polygon[(index + 1) % polygon.Count];
                Vector2 segment = second - first;
                float denominator = segment.sqrMagnitude;
                float amount = denominator <= 0.000001f
                    ? 0f
                    : Mathf.Clamp01(
                        Vector2.Dot(point - first, segment) / denominator);
                Vector2 closest = Vector2.Lerp(first, second, amount);
                bestSquared = Mathf.Min(
                    bestSquared,
                    (point - closest).sqrMagnitude);
            }

            return Mathf.Sqrt(bestSquared);
        }

        internal static void FindClosest(
            MountainRoadRoutePlan route,
            Vector2 point,
            out float distance,
            out Vector3 center,
            out Vector3 right,
            out float halfWidth)
        {
            float bestSqr = float.PositiveInfinity;
            center = route.Start;
            right = route.Samples[0].Right;
            halfWidth = route.Samples[0].Width * 0.5f;
            for (int index = 1; index < route.Samples.Count; index++)
            {
                MountainRoadRouteSample first = route.Samples[index - 1];
                MountainRoadRouteSample second = route.Samples[index];
                Vector2 a = new Vector2(first.Position.x, first.Position.z);
                Vector2 b = new Vector2(second.Position.x, second.Position.z);
                Vector2 ab = b - a;
                float denominator = ab.sqrMagnitude;
                float t = denominator <= 0.000001f
                    ? 0f
                    : Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
                Vector2 closest = Vector2.Lerp(a, b, t);
                float sqr = (point - closest).sqrMagnitude;
                if (sqr >= bestSqr)
                {
                    continue;
                }

                bestSqr = sqr;
                center = Vector3.Lerp(first.Position, second.Position, t);
                Vector3 forward = Vector3.Slerp(
                    first.Forward,
                    second.Forward,
                    t).normalized;
                right = Vector3.Cross(Vector3.up, forward).normalized;
                halfWidth = Mathf.Lerp(first.Width, second.Width, t) * 0.5f;
            }

            distance = Mathf.Sqrt(bestSqr);
        }
    }
}
