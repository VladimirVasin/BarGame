using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum MountainCablewayLoopSection
    {
        AscendingTrack = 0,
        UpperTurn = 1,
        DescendingTrack = 2,
        LowerTurn = 3
    }

    /// <summary>
    /// A cable attachment sample. The cabin body hangs below Position;
    /// Tangent follows the real, continuous loop rather than jumping between
    /// the two visible tracks at either terminal.
    /// </summary>
    public readonly struct MountainCablewayMotionSample
    {
        internal MountainCablewayMotionSample(
            Vector3 position,
            Vector3 tangent,
            float loopDistance,
            float lineDistance,
            int trackSide,
            MountainCablewayLoopSection section)
        {
            Position = position;
            Tangent = tangent.normalized;
            LoopDistance = loopDistance;
            LineDistance = lineDistance;
            TrackSide = trackSide;
            Section = section;
        }

        public Vector3 Position { get; }
        public Vector3 Tangent { get; }
        public float LoopDistance { get; }
        public float LineDistance { get; }

        /// <summary>
        /// +1 is the ascending track, -1 the descending track and 0 a
        /// terminal turn.
        /// </summary>
        public int TrackSide { get; }

        public MountainCablewayLoopSection Section { get; }
    }

    /// <summary>
    /// Pure cableway sampling. Straight spans use a restrained parabolic sag
    /// between authored supports. Both terminals are genuine half circles,
    /// so a moving cabin never teleports between tracks.
    /// </summary>
    public static class MountainCablewayMotion
    {
        public const float SagPerMeter = 0.034f;
        public const float MaximumSpanSag = 0.82f;

        public static MountainCablewayMotionSample Sample(
            MountainRoadCablewayPlan plan,
            float unwrappedLoopDistance)
        {
            RequirePlan(plan);
            float distance = WrapDistance(
                unwrappedLoopDistance,
                plan.LoopLength);
            float radius = plan.TurnRadius;
            float turnLength = Mathf.PI * radius;
            float ascendingEnd = plan.LineLength;
            float upperTurnEnd = ascendingEnd + turnLength;
            float descendingEnd = upperTurnEnd + plan.LineLength;

            if (distance < ascendingEnd)
            {
                return SampleTrack(
                    plan,
                    distance,
                    1,
                    false,
                    distance,
                    MountainCablewayLoopSection.AscendingTrack);
            }

            if (distance < upperTurnEnd)
            {
                float amount = (distance - ascendingEnd) / turnLength;
                float angle = amount * Mathf.PI;
                Vector3 center = plan.UpperCableCenter;
                Vector3 position = center +
                    plan.LineRight * (Mathf.Cos(angle) * radius) +
                    plan.LineForward * (Mathf.Sin(angle) * radius);
                Vector3 tangent =
                    -plan.LineRight * Mathf.Sin(angle) +
                    plan.LineForward * Mathf.Cos(angle);
                return new MountainCablewayMotionSample(
                    position,
                    tangent,
                    distance,
                    plan.LineLength,
                    0,
                    MountainCablewayLoopSection.UpperTurn);
            }

            if (distance < descendingEnd)
            {
                float travelledDown = distance - upperTurnEnd;
                float lineDistance = plan.LineLength - travelledDown;
                return SampleTrack(
                    plan,
                    lineDistance,
                    -1,
                    true,
                    distance,
                    MountainCablewayLoopSection.DescendingTrack);
            }

            float lowerAmount = (distance - descendingEnd) / turnLength;
            float lowerAngle = lowerAmount * Mathf.PI;
            Vector3 lowerCenter = plan.LowerCableCenter;
            Vector3 lowerPosition = lowerCenter -
                plan.LineRight * (Mathf.Cos(lowerAngle) * radius) -
                plan.LineForward * (Mathf.Sin(lowerAngle) * radius);
            Vector3 lowerTangent =
                plan.LineRight * Mathf.Sin(lowerAngle) -
                plan.LineForward * Mathf.Cos(lowerAngle);
            return new MountainCablewayMotionSample(
                lowerPosition,
                lowerTangent,
                distance,
                0f,
                0,
                MountainCablewayLoopSection.LowerTurn);
        }

        public static Vector3 SampleTrackPosition(
            MountainRoadCablewayPlan plan,
            float lineDistance,
            int trackSide)
        {
            RequireTrackSide(trackSide);
            return SampleTrack(
                    plan,
                    lineDistance,
                    trackSide,
                    false,
                    0f,
                    trackSide > 0
                        ? MountainCablewayLoopSection.AscendingTrack
                        : MountainCablewayLoopSection.DescendingTrack)
                .Position;
        }

        public static Vector3 SampleTrackTangent(
            MountainRoadCablewayPlan plan,
            float lineDistance,
            int travelDirection)
        {
            RequireTrackSide(travelDirection);
            MountainCablewayMotionSample sample = SampleTrack(
                plan,
                lineDistance,
                travelDirection,
                travelDirection < 0,
                0f,
                travelDirection > 0
                    ? MountainCablewayLoopSection.AscendingTrack
                    : MountainCablewayLoopSection.DescendingTrack);
            return sample.Tangent;
        }

        public static float WrapDistance(float distance, float loopLength)
        {
            if (!IsFinite(distance))
            {
                throw new ArgumentOutOfRangeException(nameof(distance));
            }

            if (!IsFinite(loopLength) || loopLength <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(loopLength));
            }

            return Mathf.Repeat(distance, loopLength);
        }

        /// <summary>
        /// Counts forward crossings of one loop marker. This lets presentation
        /// play a mechanical clack only when a cabin really passes a roller,
        /// including a frame that wraps through zero.
        /// </summary>
        public static int CountForwardCrossings(
            float previousUnwrappedDistance,
            float currentUnwrappedDistance,
            float markerDistance,
            float loopLength)
        {
            if (!IsFinite(previousUnwrappedDistance) ||
                !IsFinite(currentUnwrappedDistance) ||
                !IsFinite(markerDistance) ||
                !IsFinite(loopLength) ||
                loopLength <= 0f ||
                currentUnwrappedDistance < previousUnwrappedDistance)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(currentUnwrappedDistance));
            }

            float marker = WrapDistance(markerDistance, loopLength);
            int before = Mathf.FloorToInt(
                (previousUnwrappedDistance - marker) / loopLength);
            int after = Mathf.FloorToInt(
                (currentUnwrappedDistance - marker) / loopLength);
            return Mathf.Max(0, after - before);
        }

        private static MountainCablewayMotionSample SampleTrack(
            MountainRoadCablewayPlan plan,
            float requestedLineDistance,
            int trackSide,
            bool reverseTangent,
            float loopDistance,
            MountainCablewayLoopSection section)
        {
            RequirePlan(plan);
            RequireTrackSide(trackSide);
            float distance = Mathf.Clamp(
                requestedLineDistance,
                0f,
                plan.LineLength);
            IReadOnlyList<MountainCablewayNodeDescriptor> nodes = plan.Nodes;
            int segment = nodes.Count - 2;
            for (int index = 0; index < nodes.Count - 1; index++)
            {
                if (distance <= nodes[index + 1].Distance)
                {
                    segment = index;
                    break;
                }
            }

            MountainCablewayNodeDescriptor first = nodes[segment];
            MountainCablewayNodeDescriptor second = nodes[segment + 1];
            float span = Mathf.Max(
                0.0001f,
                second.Distance - first.Distance);
            float amount = Mathf.Clamp01(
                (distance - first.Distance) / span);
            float sag = Mathf.Min(MaximumSpanSag, span * SagPerMeter);
            float sagOffset = -4f * sag * amount * (1f - amount);
            Vector3 center = Vector3.Lerp(
                                 first.CableCenter,
                                 second.CableCenter,
                                 amount) +
                             Vector3.up * sagOffset;
            Vector3 derivative =
                (second.CableCenter - first.CableCenter) / span +
                Vector3.down * (4f * sag * (1f - 2f * amount) / span);
            Vector3 tangent = derivative.normalized;
            if (reverseTangent)
            {
                tangent = -tangent;
            }

            return new MountainCablewayMotionSample(
                center + plan.LineRight *
                (trackSide * plan.TrackSeparation * 0.5f),
                tangent,
                loopDistance,
                distance,
                trackSide,
                section);
        }

        private static void RequirePlan(MountainRoadCablewayPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (plan.Nodes.Count < 2 ||
                plan.LineLength <= 0f ||
                plan.TrackSeparation <= 0f ||
                plan.LoopLength <= 0f)
            {
                throw new ArgumentException(
                    "Cableway motion requires an ordered, positive loop.",
                    nameof(plan));
            }
        }

        private static void RequireTrackSide(int trackSide)
        {
            if (trackSide != -1 && trackSide != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(trackSide));
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
