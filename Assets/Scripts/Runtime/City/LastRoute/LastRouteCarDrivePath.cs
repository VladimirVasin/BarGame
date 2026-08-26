using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one place on a road where the car has to look before it goes.
    ///
    /// The city leg has exactly one: the left turn off the street into the
    /// tunnel forecourt, which crosses the oncoming lane and the pavement in
    /// front of it. The mountain leg has none - nothing lives up there and
    /// nothing else drives it.
    ///
    /// <see cref="Distance"/> is the stop line, in metres along the path, far
    /// enough back that the car is still square in its own lane when it stops.
    /// <see cref="From"/> and <see cref="To"/> are the crossing itself - the
    /// lane point the turn starts at and the forecourt mouth it ends at - and
    /// they are what an oncoming bus or a walker is measured against.
    /// </summary>
    public readonly struct LastRouteCarGiveWayPoint
    {
        public LastRouteCarGiveWayPoint(float distance, Vector3 from, Vector3 to)
        {
            IsPresent = true;
            Distance = distance;
            From = from;
            To = to;
        }

        public static LastRouteCarGiveWayPoint None => default;

        public bool IsPresent { get; }
        public float Distance { get; }
        public Vector3 From { get; }
        public Vector3 To { get; }
    }

    /// <summary>
    /// One drivable centreline, measured in metres from its own start.
    ///
    /// Both of the Ferryman's legs are this same type, deliberately. The city
    /// leg is stitched together out of the lot exit, a lane laid over the
    /// layout's own street edges and the run into the tunnel; the mountain leg
    /// is <see cref="MountainRoadRoutePlan"/> read out at a metre and given a
    /// lead-in and an apron manoeuvre. Sampling one of them is not a different
    /// operation from sampling the other, so there is one class and one set of
    /// tests rather than an interface with two shapes behind it. A road may
    /// also carry one <see cref="LastRouteCarGiveWayPoint"/>; the city leg
    /// does and the mountain leg does not.
    ///
    /// Forward is kept in THREE dimensions - the climb is 26 m over 620 and a
    /// car that drives up it level reads as a hovercraft. Curvature, on the
    /// other hand, is measured on the ground plane only, because what limits
    /// cornering speed is how hard the road turns, not how steeply it rises.
    /// The price of keeping the pitch is that two points sharing an X and a Z
    /// but not a Y are a segment pointing straight UP, and
    /// <see cref="BuildVertexForwards"/> averages it into a forward pitched
    /// forty-five degrees. Nothing here can catch that - curvature is planar -
    /// so a planner must not hand this a vertical step it did not mean.
    /// </summary>
    public sealed class LastRouteCarDrivePath
    {
        /// <summary>
        /// Two points closer together than this are the same point. A road
        /// welded out of three sources carries a duplicate at every seam, and
        /// the corner rounder puts its own arc ends within a millimetre of a
        /// corner it barely cuts.
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

        /// <summary>
        /// The one place on this road where the car gives way, or
        /// <see cref="LastRouteCarGiveWayPoint.None"/> if it never has to.
        /// </summary>
        public LastRouteCarGiveWayPoint GiveWay { get; private set; }

        public Vector3 GetPoint(int index) => points[index];
        public float GetDistance(int index) => distances[index];

        /// <summary>
        /// Names the one place on this road where the car has to look before
        /// it goes.
        ///
        /// Declared after construction, and only by the planner that laid the
        /// road, because the line is measured in METRES ALONG it: rounding a
        /// corner changes the arc length either side of it, so there is
        /// nothing to measure against until the road exists.
        /// </summary>
        public void DeclareGiveWay(LastRouteCarGiveWayPoint giveWay)
        {
            if (GiveWay.IsPresent)
            {
                throw new InvalidOperationException(
                    "A road gives way in one place at most.");
            }

            if (!giveWay.IsPresent)
            {
                return;
            }

            GiveWay = new LastRouteCarGiveWayPoint(
                Mathf.Clamp(Sanitize(giveWay.Distance), 0f, Length),
                giveWay.From,
                giveWay.To);
        }

        /// <summary>
        /// How far along this road the point on it nearest a place in the
        /// world lies.
        ///
        /// This is how a planner's world-space stop line becomes the one
        /// number the drive model understands. It measures against the
        /// SEGMENTS rather than the vertices, because the road is sampled
        /// every metre and a half and half of that is most of a car.
        /// </summary>
        public float FindNearestDistance(Vector3 point)
        {
            float best = float.PositiveInfinity;
            float found = 0f;
            for (int index = 0; index < points.Length - 1; index++)
            {
                Vector3 from = points[index];
                Vector3 run = points[index + 1] - from;
                float lengthSquared = run.sqrMagnitude;
                float t = lengthSquared > 0.000001f
                    ? Mathf.Clamp01(
                        Vector3.Dot(point - from, run) / lengthSquared)
                    : 0f;
                Vector3 candidate = from + (run * t);
                float distance = (candidate - point).sqrMagnitude;
                if (distance >= best)
                {
                    continue;
                }

                best = distance;
                found = Mathf.Lerp(
                    distances[index],
                    distances[index + 1],
                    t);
            }

            return found;
        }

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
