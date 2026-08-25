using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One drivable centreline, measured in metres from its own start.
    ///
    /// Both of the Ferryman's legs are this same type, deliberately. The city
    /// leg is stitched together out of the bus graph's baked link samples, the
    /// lot exit and the run into the tunnel; the mountain leg is
    /// <see cref="MountainRoadRoutePlan"/> read out at a metre and given a
    /// lead-in and an apron manoeuvre. Sampling one of them is not a different
    /// operation from sampling the other, so there is one class and one set of
    /// tests rather than an interface with two shapes behind it.
    ///
    /// Forward is kept in THREE dimensions - the climb is 26 m over 620 and a
    /// car that drives up it level reads as a hovercraft. Curvature, on the
    /// other hand, is measured on the ground plane only, because what limits
    /// cornering speed is how hard the road turns, not how steeply it rises.
    /// </summary>
    public sealed class LastRouteCarDrivePath
    {
        /// <summary>
        /// Two points closer together than this are the same point. The bus
        /// links are baked at about a decimetre and abut each other exactly,
        /// so a naive concatenation carries a duplicate at every seam.
        /// </summary>
        public const float MinimumSegmentLength = 0.001f;

        private readonly Vector3[] points;
        private readonly float[] distances;
        private readonly Vector3[] forwards;
        private readonly float[] turnRates;

        public LastRouteCarDrivePath(IReadOnlyList<Vector3> sourcePoints)
        {
            if (sourcePoints == null)
            {
                throw new ArgumentNullException(nameof(sourcePoints));
            }

            points = Weld(sourcePoints);
            if (points.Length < 2)
            {
                throw new ArgumentException(
                    "A drivable path needs at least two distinct points.",
                    nameof(sourcePoints));
            }

            distances = new float[points.Length];
            for (int index = 1; index < points.Length; index++)
            {
                distances[index] = distances[index - 1] +
                                   Vector3.Distance(
                                       points[index - 1],
                                       points[index]);
            }

            forwards = BuildVertexForwards(points);
            turnRates = BuildTurnRates(points, distances);
        }

        public float Length => distances[distances.Length - 1];
        public int PointCount => points.Length;
        public Vector3 Start => points[0];
        public Vector3 End => points[points.Length - 1];

        public Vector3 GetPoint(int index) => points[index];
        public float GetDistance(int index) => distances[index];

        /// <summary>
        /// Where the car is and which way it is pointing, at a distance from
        /// the start. Clamped at both ends: before the start it sits on the
        /// first point facing the way the path leaves it, and past the end it
        /// sits on the last one still facing the way it arrived.
        /// </summary>
        public void Sample(
            float distance,
            out Vector3 position,
            out Vector3 forward)
        {
            float clamped = Mathf.Clamp(
                Sanitize(distance),
                0f,
                Length);
            int low = FindSegment(clamped);
            float span = distances[low + 1] - distances[low];
            float t = span > MinimumSegmentLength
                ? Mathf.Clamp01((clamped - distances[low]) / span)
                : 0f;
            position = Vector3.Lerp(points[low], points[low + 1], t);
            forward = Vector3.Slerp(
                forwards[low],
                forwards[low + 1],
                t).normalized;
        }

        /// <summary>
        /// The sharpest the road turns anywhere in a window ahead, in degrees
        /// of heading per metre travelled. This is what the drive model brakes
        /// against, and measuring it over a WINDOW rather than at a point is
        /// the whole reason the car slows down before a hairpin instead of
        /// discovering it halfway round.
        /// </summary>
        public float MaximumTurnRate(float fromDistance, float toDistance)
        {
            float from = Mathf.Clamp(Sanitize(fromDistance), 0f, Length);
            float to = Mathf.Clamp(Sanitize(toDistance), 0f, Length);
            if (to < from)
            {
                (from, to) = (to, from);
            }

            float worst = 0f;
            for (int index = 0; index < points.Length; index++)
            {
                if (distances[index] < from)
                {
                    continue;
                }

                if (distances[index] > to)
                {
                    break;
                }

                worst = Mathf.Max(worst, turnRates[index]);
            }

            return worst;
        }

        /// <summary>
        /// The turn rate at one vertex, exposed for the tests that prove a
        /// hairpin is actually recognised as one.
        /// </summary>
        public float GetTurnRate(int index) => turnRates[index];

        /// <summary>
        /// The first vertex at or past a distance, by binary search.
        ///
        /// This exists so the drive model can walk the vertices inside its own
        /// braking horizon in one pass. Asking <see cref="MaximumTurnRate"/>
        /// once per probe instead would scan the whole path per probe and per
        /// frame, which on the mountain's six hundred vertices is quadratic
        /// work for an answer that is one linear sweep.
        /// </summary>
        public int FindFirstIndexAtOrAfter(float distance)
        {
            float clamped = Mathf.Clamp(Sanitize(distance), 0f, Length);
            int low = 0;
            int high = points.Length - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (distances[middle] < clamped)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        private int FindSegment(float distance)
        {
            int low = 0;
            int high = points.Length - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (distances[middle] <= distance)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        private static Vector3[] Weld(IReadOnlyList<Vector3> sourcePoints)
        {
            var welded = new List<Vector3>(sourcePoints.Count);
            for (int index = 0; index < sourcePoints.Count; index++)
            {
                Vector3 candidate = sourcePoints[index];
                if (!IsFinite(candidate))
                {
                    throw new ArgumentException(
                        "A drivable path cannot carry a non-finite point.",
                        nameof(sourcePoints));
                }

                if (welded.Count > 0 &&
                    Vector3.Distance(welded[welded.Count - 1], candidate) <
                    MinimumSegmentLength)
                {
                    continue;
                }

                welded.Add(candidate);
            }

            return welded.ToArray();
        }

        /// <summary>
        /// A forward per VERTEX rather than per segment, averaged from the two
        /// segments that meet there. Per-segment forwards make the car snap
        /// through every join; the bus's own path sampler carries a forward on
        /// each sample for exactly this reason.
        /// </summary>
        private static Vector3[] BuildVertexForwards(Vector3[] points)
        {
            var segmentForwards = new Vector3[points.Length - 1];
            for (int index = 0; index < segmentForwards.Length; index++)
            {
                segmentForwards[index] =
                    (points[index + 1] - points[index]).normalized;
            }

            var vertexForwards = new Vector3[points.Length];
            vertexForwards[0] = segmentForwards[0];
            vertexForwards[points.Length - 1] =
                segmentForwards[segmentForwards.Length - 1];
            for (int index = 1; index < points.Length - 1; index++)
            {
                Vector3 averaged =
                    segmentForwards[index - 1] + segmentForwards[index];
                // A perfect reversal averages to nothing. Nothing in either
                // leg should double back on itself, but a welded path is
                // built from three sources and this must not produce a zero
                // forward and a LookRotation warning if one ever does.
                vertexForwards[index] = averaged.sqrMagnitude > 0.000001f
                    ? averaged.normalized
                    : segmentForwards[index];
            }

            return vertexForwards;
        }

        private static float[] BuildTurnRates(
            Vector3[] points,
            float[] distances)
        {
            var turnRates = new float[points.Length];
            for (int index = 1; index < points.Length - 1; index++)
            {
                Vector3 incoming = Flatten(points[index] - points[index - 1]);
                Vector3 outgoing = Flatten(points[index + 1] - points[index]);
                if (incoming.sqrMagnitude < 0.000001f ||
                    outgoing.sqrMagnitude < 0.000001f)
                {
                    continue;
                }

                float turned = Vector3.Angle(incoming, outgoing);
                // Spread the corner over the ground the car actually covers
                // taking it - half of each neighbouring segment - so a road
                // sampled every metre and the same road sampled every ten
                // centimetres report the same sharpness.
                float span =
                    ((distances[index] - distances[index - 1]) +
                     (distances[index + 1] - distances[index])) * 0.5f;
                turnRates[index] = span > MinimumSegmentLength
                    ? turned / span
                    : 0f;
            }

            return turnRates;
        }

        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
