using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static partial class CityBusPlanner
    {
        // Circumscribes both the 0.42 m lower-pole collider and the rotated
        // 0.50 x 0.28 m signal housing in the horizontal plane.
        internal const float TrafficSignalFixtureRadius = 0.30f;

        private static void AddRightJunction(
            CityLayout layout,
            CityBusDesignVehicle vehicle,
            DirectedStreet incoming,
            DirectedStreet outgoing,
            bool hasFullApron,
            bool hasTrafficSignals,
            float laneCenterOffset,
            ICollection<TemporaryLink> accepted,
            ICollection<CityBusClearanceFailure> failures)
        {
            if (!hasFullApron)
            {
                AddTightRightTurnFailure(
                    layout,
                    vehicle,
                    incoming,
                    outgoing,
                    laneCenterOffset,
                    failures);
                return;
            }

            List<CityBusPathSample> samples =
                CreateWideRightTurnSamples(
                    layout,
                    incoming,
                    outgoing,
                    laneCenterOffset,
                    out float laneShiftRadius);
            var allowed = new List<Rect>
            {
                GetCarriagewayRect(layout, incoming.RoadEdge),
                GetCarriagewayRect(layout, outgoing.RoadEdge),
                GetIntersectionCoreRect(layout, incoming.To),
                GetIntersectionApproachRect(
                    layout,
                    incoming.To,
                    incoming.RoadEdge),
                GetIntersectionApproachRect(
                    layout,
                    incoming.To,
                    outgoing.RoadEdge)
            };
            CityBusClearanceResult clearance = ValidateSamples(
                vehicle,
                samples,
                allowed);
            clearance = ValidateStaticIntersectionFixtures(
                layout,
                vehicle,
                incoming.To,
                hasTrafficSignals,
                samples,
                clearance);
            float minimumRadius = Mathf.Min(
                RightTurnRadius,
                laneShiftRadius);
            string id = ManeuverId(incoming, outgoing, "right-wide");
            if (minimumRadius + GeometryTolerance >=
                    vehicle.MinimumBodyCenterTurnRadius &&
                clearance.IsClear)
            {
                accepted.Add(new TemporaryLink(
                    id,
                    incoming.DepartureNodeIndex,
                    outgoing.ArrivalNodeIndex,
                    CityBusRouteLinkKind.RightTurn,
                    incoming.To,
                    samples,
                    minimumRadius,
                    clearance,
                    false,
                    incoming.RoadEdge,
                    true,
                    true,
                    outgoing.RoadEdge));
                return;
            }

            failures.Add(CreateFailure(
                id,
                incoming,
                outgoing,
                CityBusRouteLinkKind.RightTurn,
                samples,
                minimumRadius,
                clearance));
        }

        private static CityBusClearanceResult
            ValidateStaticIntersectionFixtures(
                CityLayout layout,
                CityBusDesignVehicle vehicle,
                Vector2Int junctionNode,
                bool hasTrafficSignals,
                IList<CityBusPathSample> samples,
                CityBusClearanceResult surfaceClearance)
        {
            if (!surfaceClearance.IsClear || !hasTrafficSignals)
            {
                return surfaceClearance;
            }

            Vector3 junction = layout.GetNodeWorldPosition(junctionNode);
            Vector3 pairedOffset =
                CityStreetIntersectionSelector.GetPairedFixtureOffset(
                    layout,
                    junctionNode);
            Vector3 firstFixture = junction + pairedOffset;
            Vector3 secondFixture = junction - pairedOffset;
            float halfLength = vehicle.InflatedLength * 0.5f;
            float halfWidth = vehicle.InflatedWidth * 0.5f;
            float radiusSquared =
                TrafficSignalFixtureRadius * TrafficSignalFixtureRadius;
            for (int sampleIndex = 0;
                 sampleIndex < samples.Count;
                 sampleIndex++)
            {
                CityBusPathSample sample = samples[sampleIndex];
                Vector3 forward = sample.Forward;
                forward.y = 0f;
                if (forward.sqrMagnitude <= GeometryTolerance)
                {
                    continue;
                }

                forward.Normalize();
                Vector3 right = new Vector3(
                    forward.z,
                    0f,
                    -forward.x);
                if (FixtureOverlapsInflatedBody(
                        sample.Position,
                        forward,
                        right,
                        halfLength,
                        halfWidth,
                        firstFixture,
                        radiusSquared))
                {
                    return new CityBusClearanceResult(
                        false,
                        CityBusClearanceFailureKind.StaticFixtureOverlap,
                        sampleIndex,
                        firstFixture,
                        0f);
                }

                if (FixtureOverlapsInflatedBody(
                        sample.Position,
                        forward,
                        right,
                        halfLength,
                        halfWidth,
                        secondFixture,
                        radiusSquared))
                {
                    return new CityBusClearanceResult(
                        false,
                        CityBusClearanceFailureKind.StaticFixtureOverlap,
                        sampleIndex,
                        secondFixture,
                        0f);
                }
            }

            return surfaceClearance;
        }

        private static bool FixtureOverlapsInflatedBody(
            Vector3 bodyCenter,
            Vector3 forward,
            Vector3 right,
            float halfLength,
            float halfWidth,
            Vector3 fixtureCenter,
            float fixtureRadiusSquared)
        {
            Vector3 delta = fixtureCenter - bodyCenter;
            delta.y = 0f;
            float outsideForward = Mathf.Max(
                0f,
                Mathf.Abs(Vector3.Dot(delta, forward)) - halfLength);
            float outsideRight = Mathf.Max(
                0f,
                Mathf.Abs(Vector3.Dot(delta, right)) - halfWidth);
            return outsideForward * outsideForward +
                       outsideRight * outsideRight <=
                   fixtureRadiusSquared +
                   (GeometryTolerance * GeometryTolerance);
        }

        private static void AddTightRightTurnFailure(
            CityLayout layout,
            CityBusDesignVehicle vehicle,
            DirectedStreet incoming,
            DirectedStreet outgoing,
            float laneCenterOffset,
            ICollection<CityBusClearanceFailure> failures)
        {
            float cornerOffset =
                (layout.RoadWidth * 0.5f) - TurnTangentInset;
            float radius = Mathf.Max(
                0f,
                cornerOffset - laneCenterOffset);
            List<CityBusPathSample> samples = CreateTurnSamples(
                layout,
                incoming,
                outgoing,
                laneCenterOffset,
                false,
                radius);
            var clearance = new CityBusClearanceResult(
                false,
                CityBusClearanceFailureKind.CurvatureTooTight,
                0,
                samples.Count > 0
                    ? samples[0].Position
                    : layout.GetNodeWorldPosition(incoming.To),
                0f);
            failures.Add(CreateFailure(
                ManeuverId(incoming, outgoing, "right-tight"),
                incoming,
                outgoing,
                CityBusRouteLinkKind.RightTurn,
                samples,
                radius,
                clearance));
        }

        private static List<CityBusPathSample>
            CreateWideRightTurnSamples(
                CityLayout layout,
                DirectedStreet incoming,
                DirectedStreet outgoing,
                float laneCenterOffset,
                out float minimumLaneShiftRadius)
        {
            var result = new List<CityBusPathSample>();
            Vector3 junction = layout.GetNodeWorldPosition(incoming.To);
            junction.y += CityStreetSurfacePlanner.RoadTop;
            float halfRoad = layout.RoadWidth * 0.5f;
            float cornerOffset = halfRoad - TurnTangentInset;

            Vector3 start = GetDeparturePosition(layout, incoming);
            Vector3 incomingTangent = junction -
                (incoming.Forward * cornerOffset);
            float incomingShiftLength = Vector3.Dot(
                incomingTangent - start,
                incoming.Forward);
            float incomingShiftAngle = CalculateLaneShiftAngle(
                incomingShiftLength,
                laneCenterOffset);
            float incomingShiftRadius = CalculateLaneShiftRadius(
                incomingShiftLength,
                incomingShiftAngle);

            AppendTurnArc(
                result,
                start,
                incoming.Forward,
                true,
                incomingShiftRadius,
                incomingShiftAngle);
            CityBusPathSample firstShift = result[result.Count - 1];
            AppendTurnArc(
                result,
                firstShift.Position,
                firstShift.Forward,
                false,
                incomingShiftRadius,
                incomingShiftAngle);
            AppendLinearCorrection(
                result,
                incomingTangent,
                incoming.Forward);
            int incomingEndIndex = result.Count - 1;

            AppendTurnArc(
                result,
                incomingTangent,
                incoming.Forward,
                false,
                RightTurnRadius,
                Mathf.PI * 0.5f);
            Vector3 outgoingTangent = junction +
                (outgoing.Forward * cornerOffset);
            AppendLinearCorrection(
                result,
                outgoingTangent,
                outgoing.Forward);
            int turnEndIndex = result.Count - 1;

            Vector3 end = GetArrivalPosition(layout, outgoing);
            float outgoingShiftLength = Vector3.Dot(
                end - outgoingTangent,
                outgoing.Forward);
            float outgoingShiftAngle = CalculateLaneShiftAngle(
                outgoingShiftLength,
                laneCenterOffset);
            float outgoingShiftRadius = CalculateLaneShiftRadius(
                outgoingShiftLength,
                outgoingShiftAngle);
            minimumLaneShiftRadius = Mathf.Min(
                incomingShiftRadius,
                outgoingShiftRadius);
            CityBusPathSample turnEnd = result[result.Count - 1];
            AppendTurnArc(
                result,
                turnEnd.Position,
                turnEnd.Forward,
                false,
                outgoingShiftRadius,
                outgoingShiftAngle);
            CityBusPathSample secondShift = result[result.Count - 1];
            AppendTurnArc(
                result,
                secondShift.Position,
                secondShift.Forward,
                true,
                outgoingShiftRadius,
                outgoingShiftAngle);
            AppendLinearCorrection(
                result,
                end,
                outgoing.Forward);
            ApplyRoadGrade(
                result,
                0,
                incomingEndIndex,
                start,
                junction - (incoming.Forward * halfRoad),
                incoming.Forward);
            FlattenNodePad(
                result,
                incomingEndIndex + 1,
                turnEndIndex - 1,
                junction.y);
            ApplyRoadGrade(
                result,
                turnEndIndex,
                result.Count - 1,
                junction + (outgoing.Forward * halfRoad),
                end,
                outgoing.Forward);
            RebuildSampleDistances(result);
            return result;
        }

        private static void ApplyRoadGrade(
            IList<CityBusPathSample> samples,
            int firstIndex,
            int lastIndex,
            Vector3 start,
            Vector3 end,
            Vector3 roadForward)
        {
            if (firstIndex < 0 ||
                lastIndex >= samples.Count ||
                firstIndex > lastIndex)
            {
                return;
            }

            Vector3 planarRoadForward = roadForward;
            planarRoadForward.y = 0f;
            planarRoadForward.Normalize();
            float horizontalRun = Vector3.Dot(
                end - start,
                planarRoadForward);
            float elevationDelta = end.y - start.y;
            float risePerMeter = Mathf.Abs(horizontalRun) >
                                 GeometryTolerance
                ? elevationDelta / horizontalRun
                : 0f;
            for (int index = firstIndex; index <= lastIndex; index++)
            {
                CityBusPathSample sample = samples[index];
                Vector3 position = sample.Position;
                float rawAmount = Mathf.Abs(horizontalRun) >
                                  GeometryTolerance
                    ? Vector3.Dot(
                        position - start,
                        planarRoadForward) /
                      horizontalRun
                    : 0f;
                float amount = Mathf.Clamp01(rawAmount);
                position.y = Mathf.Lerp(start.y, end.y, amount);

                Vector3 planarForward = sample.Forward;
                planarForward.y = 0f;
                planarForward = planarForward.sqrMagnitude > 0.0001f
                    ? planarForward.normalized
                    : planarRoadForward;
                float tangentRise = rawAmount > GeometryTolerance &&
                                    rawAmount < 1f - GeometryTolerance
                    ? risePerMeter * Vector3.Dot(
                        planarForward,
                        planarRoadForward)
                    : 0f;
                Vector3 gradedForward =
                    planarForward + (Vector3.up * tangentRise);
                samples[index] = new CityBusPathSample(
                    position,
                    gradedForward.normalized,
                    sample.Distance);
            }
        }

        private static void FlattenNodePad(
            IList<CityBusPathSample> samples,
            int firstIndex,
            int lastIndex,
            float elevation)
        {
            for (int index = Mathf.Max(0, firstIndex);
                 index <= lastIndex && index < samples.Count;
                 index++)
            {
                CityBusPathSample sample = samples[index];
                Vector3 position = sample.Position;
                position.y = elevation;
                Vector3 forward = sample.Forward;
                forward.y = 0f;
                samples[index] = new CityBusPathSample(
                    position,
                    forward.sqrMagnitude > 0.0001f
                        ? forward.normalized
                        : Vector3.forward,
                    sample.Distance);
            }
        }

        private static void RebuildSampleDistances(
            IList<CityBusPathSample> samples)
        {
            float distance = 0f;
            for (int index = 0; index < samples.Count; index++)
            {
                CityBusPathSample sample = samples[index];
                if (index > 0)
                {
                    distance += Vector3.Distance(
                        samples[index - 1].Position,
                        sample.Position);
                }

                samples[index] = new CityBusPathSample(
                    sample.Position,
                    sample.Forward,
                    distance);
            }
        }

        private static float CalculateLaneShiftAngle(
            float length,
            float lateralOffset)
        {
            if (length <= GeometryTolerance ||
                lateralOffset <= GeometryTolerance)
            {
                return 0f;
            }

            return 2f * Mathf.Atan(lateralOffset / length);
        }

        private static float CalculateLaneShiftRadius(
            float length,
            float angle)
        {
            if (angle <= GeometryTolerance)
            {
                return float.PositiveInfinity;
            }

            return length / (2f * Mathf.Sin(angle));
        }

        private static void AppendTurnArc(
            IList<CityBusPathSample> target,
            Vector3 start,
            Vector3 startForward,
            bool leftTurn,
            float radius,
            float angle)
        {
            if (angle <= GeometryTolerance ||
                float.IsInfinity(radius))
            {
                if (target.Count == 0)
                {
                    target.Add(new CityBusPathSample(
                        start,
                        startForward,
                        0f));
                }

                return;
            }

            Vector3 forward = startForward.normalized;
            Vector3 right = new Vector3(
                forward.z,
                0f,
                -forward.x);
            Vector3 center = leftTurn
                ? start - (right * radius)
                : start + (right * radius);
            if (target.Count == 0)
            {
                target.Add(new CityBusPathSample(
                    start,
                    forward,
                    0f));
            }

            int steps = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (radius * angle) / PathSampleSpacing));
            for (int step = 1; step <= steps; step++)
            {
                float sampleAngle = angle * (step / (float)steps);
                float cosine = Mathf.Cos(sampleAngle);
                float sine = Mathf.Sin(sampleAngle);
                Vector3 offset;
                Vector3 tangent;
                if (leftTurn)
                {
                    offset = (right * cosine + forward * sine) * radius;
                    tangent = forward * cosine - right * sine;
                }
                else
                {
                    offset = (-right * cosine + forward * sine) * radius;
                    tangent = forward * cosine + right * sine;
                }

                AppendPoint(
                    target,
                    center + offset,
                    tangent.normalized);
            }
        }

        private static void AppendLinearCorrection(
            IList<CityBusPathSample> target,
            Vector3 expectedPosition,
            Vector3 expectedForward)
        {
            CityBusPathSample last = target[target.Count - 1];
            if (Vector3.Distance(
                    last.Position,
                    expectedPosition) > GeometryTolerance)
            {
                AppendLinear(
                    target,
                    last.Position,
                    expectedPosition,
                    expectedForward);
                return;
            }

            if (Vector3.Distance(
                    last.Forward,
                    expectedForward) > GeometryTolerance)
            {
                AppendPoint(
                    target,
                    expectedPosition,
                    expectedForward);
            }
        }
    }
}
