using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class MountainRoadTerminalValidator
    {
        private const float PositionTolerance = 0.03f;
        private const float VehicleObjectClearance = 0.55f;

        public static void ValidateOrThrow(MountainRoadPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            MountainRoadTerminalPlan terminal = plan.Terminal ??
                throw new InvalidOperationException(
                    "The mountain endpoint requires a terminal plan.");
            ValidateVehicleApron(plan, terminal.VehicleApron);
            ValidateCafe(plan, terminal.Cafe, terminal.VehicleApron);
            ValidateCableway(plan, terminal.Cableway, terminal.VehicleApron);
            ValidateLandmarkSeparation(terminal);
            ValidateLandmarks(plan, terminal.Landmarks);
            ValidateTerminalExclusions(plan, terminal.Cableway);
            MountainRoadTerminalSiteValidator.ValidateOrThrow(plan);
        }

        private static void ValidateVehicleApron(
            MountainRoadPlan plan,
            MountainRoadVehicleApronPlan apron)
        {
            RequireFinite(apron.Center, "Vehicle apron center");
            RequireFinite(apron.EntryCenter, "Vehicle apron entry");
            RequireFinite(apron.Forward, "Vehicle apron forward");
            if (apron.EntryWidth < MountainRoadPlanner.RoadWidth - 0.01f ||
                apron.TurningRadius < 7.49f)
            {
                throw new InvalidOperationException(
                    "The terminal must retain the full automotive entry and " +
                    "7.5 m turning circle.");
            }

            const int ringSamples = 48;
            for (int index = 0; index < ringSamples; index++)
            {
                float angle = index / (float)ringSamples * Mathf.PI * 2f;
                Vector3 point = apron.Center +
                    apron.Right * (Mathf.Cos(angle) * apron.TurningRadius) +
                    apron.Forward * (Mathf.Sin(angle) * apron.TurningRadius);
                if (!plan.Plateau.Contains(ToXZ(point)))
                {
                    throw new InvalidOperationException(
                        "The vehicle turning circle leaves the terminal " +
                        "plateau.");
                }
            }

            MountainRoadRouteSample entry = plan.Route.Sample(
                plan.Plateau.EntryDistance);
            if (Vector3.Distance(entry.Position, apron.EntryCenter) >
                PositionTolerance)
            {
                throw new InvalidOperationException(
                    "The automotive apron is detached from the road seam.");
            }
        }

        private static void ValidateCafe(
            MountainRoadPlan plan,
            MountainRoadCafePlan cafe,
            MountainRoadVehicleApronPlan apron)
        {
            if (string.IsNullOrWhiteSpace(cafe.StableId) ||
                cafe.FootprintXZ.Count != 5 ||
                cafe.Height < 4.3f ||
                cafe.DoorWidth < 1.59f)
            {
                throw new InvalidOperationException(
                    "The terminal cafe must keep its authored five-sided, " +
                    "walk-in envelope.");
            }

            RequireFinite(cafe.Center, "Cafe center");
            RequireFinite(cafe.DoorCenter, "Cafe door");
            if (Vector3.Dot(cafe.Center - apron.Center, apron.Right) > -8f)
            {
                throw new InvalidOperationException(
                    "The cafe must stay on the left side of the arrival.");
            }

            for (int index = 0; index < cafe.FootprintXZ.Count; index++)
            {
                Vector2 point = cafe.FootprintXZ[index];
                if (!plan.Plateau.Contains(point))
                {
                    throw new InvalidOperationException(
                        "The cafe footprint leaves the physical plateau.");
                }

            }

            ValidatePolygonOutsideCircle(
                cafe.FootprintXZ,
                ToXZ(apron.Center),
                apron.TurningRadius + VehicleObjectClearance,
                "The cafe enters the reserved vehicle circle.");

            if (!plan.Plateau.Contains(ToXZ(cafe.DoorCenter)) ||
                DistanceToPolygonEdge(
                    cafe.FootprintXZ,
                    ToXZ(cafe.DoorCenter)) > PositionTolerance)
            {
                throw new InvalidOperationException(
                    "The cafe entrance is not authored on a reachable " +
                    "facade edge.");
            }

            Vector3 approach = cafe.DoorCenter +
                               cafe.DoorForward * 1.25f;
            approach.y = cafe.FloorY + 1f;
            if (!plan.Plateau.Contains(ToXZ(approach)) ||
                cafe.ContainsInterior(approach, 0f))
            {
                throw new InvalidOperationException(
                    "The cafe entrance lacks a clear exterior approach.");
            }
        }

        private static void ValidateCableway(
            MountainRoadPlan plan,
            MountainRoadCablewayPlan cableway,
            MountainRoadVehicleApronPlan apron)
        {
            if (string.IsNullOrWhiteSpace(cableway.StableId) ||
                cableway.Nodes.Count != 5 ||
                cableway.Cabins.Count != 4 ||
                cableway.LineLength < 57.9f ||
                cableway.TrackSeparation < 2.89f ||
                cableway.CabinSpeed <= 0f)
            {
                throw new InvalidOperationException(
                    "The terminal cableway lost its authored operating " +
                    "contract.");
            }

            RequireFinite(cableway.LineForward, "Cable line forward");
            RequireFinite(cableway.LineRight, "Cable line right");
            RequireFinite(cableway.CabinSize, "Cable cabin size");
            if (cableway.LineForward.sqrMagnitude < 0.99f ||
                cableway.LineRight.sqrMagnitude < 0.99f ||
                Mathf.Abs(Vector3.Dot(
                    cableway.LineForward,
                    cableway.LineRight)) > 0.01f ||
                cableway.CabinSize.x <= 0f ||
                cableway.CabinSize.y <= 0f ||
                cableway.CabinSize.z <= 0f)
            {
                throw new InvalidOperationException(
                    "Cableway axes and cabin envelope must remain valid.");
            }

            RequireFinite(cableway.StationArea.Center, "Cable station");
            if (Vector3.Dot(
                    cableway.StationArea.Center - apron.Center,
                    apron.Right) < 8f)
            {
                throw new InvalidOperationException(
                    "The cable station must stay on the right side of the " +
                    "arrival.");
            }

            int[] stationCornerOrder = { 0, 1, 3, 2 };
            var stationCorners = new Vector2[4];
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 point = cableway.StationArea.GetCorner(
                    stationCornerOrder[corner]);
                stationCorners[corner] = ToXZ(point);
                if (!plan.Plateau.Contains(ToXZ(point)))
                {
                    throw new InvalidOperationException(
                        "The lower cable station leaves the plateau.");
                }
            }

            ValidatePolygonOutsideCircle(
                stationCorners,
                ToXZ(apron.Center),
                apron.TurningRadius + VehicleObjectClearance,
                "The cable station enters the vehicle circle.");

            float previousDistance = -1f;
            float previousHeight = float.NegativeInfinity;
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            MountainCablewayNodeKind[] expectedKinds =
            {
                MountainCablewayNodeKind.LowerStation,
                MountainCablewayNodeKind.Support,
                MountainCablewayNodeKind.Support,
                MountainCablewayNodeKind.Support,
                MountainCablewayNodeKind.UpperTurn
            };
            for (int index = 0; index < cableway.Nodes.Count; index++)
            {
                MountainCablewayNodeDescriptor node = cableway.Nodes[index];
                if (string.IsNullOrWhiteSpace(node.StableId) ||
                    !nodeIds.Add(node.StableId) ||
                    node.Kind != expectedKinds[index] ||
                    node.Distance <= previousDistance ||
                    node.CableCenter.y < previousHeight - 0.01f)
                {
                    throw new InvalidOperationException(
                        "Cableway nodes must be unique, ordered and climb " +
                        "toward the mountain.");
                }

                RequireFinite(node.CableCenter, node.StableId);
                RequireFinite(node.GroundPosition, node.StableId + " ground");
                Vector3 expectedCenter = cableway.LowerCableCenter +
                    cableway.LineForward * node.Distance;
                if (Vector2.Distance(
                        ToXZ(expectedCenter),
                        ToXZ(node.CableCenter)) > PositionTolerance ||
                    Vector2.Distance(
                        ToXZ(node.GroundPosition),
                        ToXZ(node.CableCenter)) > PositionTolerance)
                {
                    throw new InvalidOperationException(
                        $"{node.StableId} leaves the authored cable axis.");
                }

                if (node.Kind == MountainCablewayNodeKind.Support)
                {
                    float expectedGround =
                        MountainRoadTerrainSampler.SampleHeight(
                            plan.Route,
                            plan.Plateau,
                            ToXZ(node.GroundPosition));
                    if (Mathf.Abs(expectedGround - node.GroundPosition.y) >
                        PositionTolerance)
                    {
                        throw new InvalidOperationException(
                            $"{node.StableId} is not grounded on terrain.");
                    }
                }

                previousDistance = node.Distance;
                previousHeight = node.CableCenter.y;
            }

            if (Mathf.Abs(cableway.Nodes[0].Distance) > PositionTolerance ||
                Mathf.Abs(previousDistance - cableway.LineLength) >
                PositionTolerance)
            {
                throw new InvalidOperationException(
                    "Cableway endpoints do not span the declared line.");
            }

            for (float distance = 0f;
                 distance <= cableway.LineLength;
                 distance += 0.5f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 attachment =
                        MountainCablewayMotion.SampleTrackPosition(
                            cableway,
                            distance,
                            side);
                    float terrain = MountainRoadTerrainSampler.SampleHeight(
                        plan.Route,
                        plan.Plateau,
                        ToXZ(attachment));
                    float clearance = attachment.y -
                                      cableway.CabinAttachmentToBottom -
                                      terrain;
                    if (clearance < 0.5f)
                    {
                        throw new InvalidOperationException(
                            "A moving cabin enters the mountain terrain at " +
                            $"{distance:0.0} m (clearance " +
                            $"{clearance:0.00} m).");
                    }
                }
            }

            var phases = new HashSet<int>();
            for (int index = 0; index < cableway.Cabins.Count; index++)
            {
                MountainCablewayCabinDescriptor cabin =
                    cableway.Cabins[index];
                int phaseKey = Mathf.RoundToInt(cabin.Phase * 10000f);
                if (string.IsNullOrWhiteSpace(cabin.StableId) ||
                    cabin.Phase < 0f || cabin.Phase >= 1f ||
                    !phases.Add(phaseKey))
                {
                    throw new InvalidOperationException(
                        "Cable cabins require four unique loop phases.");
                }
            }

            MountainRoadRidgeDescriptor occluder = FindRidge(
                plan,
                cableway.UpperOccluderStableId,
                out bool foundOccluder);
            if (!foundOccluder)
            {
                throw new InvalidOperationException(
                    "The upper gallery needs the rock behind it.");
            }

            // The rock begins at the gallery's back wall - a quarter metre
            // past it the line's axis is inside the rock, a quarter metre
            // short of it is still the gallery - and its crest stands over
            // the gallery ROOF. Against the crest that is BUILT, not the lid
            // of the box it is authored in: a ridge is a polygonal sine and
            // its middle sits about `14%` of the box below the lid.
            Vector3 insideRock = cableway.LineAxisPoint(
                cableway.UpperOccluderNearFaceDistance + 0.25f);
            Vector3 stillGallery = cableway.LineAxisPoint(
                cableway.UpperOccluderNearFaceDistance - 0.25f);
            if (!MountainRoadRidgeGeometry.TryGetCrossing(
                    occluder,
                    insideRock,
                    out float crossing) ||
                MountainRoadRidgeGeometry.TryGetCrossing(
                    occluder,
                    stillGallery,
                    out _))
            {
                throw new InvalidOperationException(
                    "The rock behind the gallery must begin at its back wall.");
            }

            if (MountainRoadRidgeGeometry.CrestWorldY(occluder, crossing) <
                cableway.UpperGalleryRoofY +
                MountainRoadCablewayPlan.UpperOccluderCrestClearance)
            {
                throw new InvalidOperationException(
                    "The rock behind the gallery must stand over its roof.");
            }

            // And the gallery stands on rock: under the floor at the line,
            // and no daylight under the plinth at any of its four corners.
            MountainRoadRidgeDescriptor pedestal = FindRidge(
                plan,
                MountainRoadCablewayPlan.UpperPedestalStableId,
                out bool foundPedestal);
            if (!foundPedestal)
            {
                throw new InvalidOperationException(
                    "The upper gallery has no rock to stand on.");
            }

            float floorY = cableway.UpperGalleryFloorY;
            float galleryMiddle =
                (cableway.UpperGalleryMouthDistance +
                 cableway.UpperGalleryBackWallDistance) * 0.5f;
            if (!MountainRoadRidgeGeometry.TryGetCrossing(
                    pedestal,
                    cableway.LineAxisPoint(galleryMiddle),
                    out float pedestalCrossing) ||
                MountainRoadRidgeGeometry.CrestWorldY(
                    pedestal,
                    pedestalCrossing) > floorY - 0.1f)
            {
                throw new InvalidOperationException(
                    "The pedestal rock breaks through the gallery floor.");
            }

            for (int corner = 0; corner < 4; corner++)
            {
                float along = (corner & 1) == 0
                    ? cableway.UpperGalleryMouthDistance
                    : cableway.UpperGalleryBackWallDistance;
                float side = (corner & 2) == 0 ? -1f : 1f;
                Vector3 cornerPoint = cableway.LineAxisPoint(along) +
                                      cableway.LineRight *
                                      (side *
                                       cableway.UpperGalleryOuterHalfWidth);
                if (!MountainRoadRidgeGeometry.TryGetCrossing(
                        pedestal,
                        cornerPoint,
                        out float cornerCrossing) ||
                    MountainRoadRidgeGeometry.CrestWorldY(
                        pedestal,
                        cornerCrossing) <
                    floorY -
                    MountainRoadCablewayPlan.UpperGalleryPlinthDepth)
                {
                    throw new InvalidOperationException(
                        "Daylight under the gallery plinth at corner " +
                        $"{corner}.");
                }
            }

            // And the ride has to be able to hide inside it: the blackout
            // must complete before the cabin's nose reaches the near face,
            // and it must still start after the last tower is passed. Between
            // those two the cabin drove into the mountain in plain sight for
            // four metres, and nothing in the suite noticed.
            float fadeStart = cableway.LineLength -
                              AlpineCablewayRideController
                                  .EvaluateFadeLeadMeters(cableway);
            float lastSupport = 0f;
            for (int index = 0; index < cableway.Nodes.Count; index++)
            {
                if (cableway.Nodes[index].Kind ==
                    MountainCablewayNodeKind.Support)
                {
                    lastSupport = Mathf.Max(
                        lastSupport,
                        cableway.Nodes[index].Distance);
                }
            }

            if (fadeStart <= lastSupport)
            {
                throw new InvalidOperationException(
                    "The cut must land after the last cable tower.");
            }
        }

        private static MountainRoadRidgeDescriptor FindRidge(
            MountainRoadPlan plan,
            string stableId,
            out bool found)
        {
            for (int index = 0; index < plan.Ridges.Count; index++)
            {
                if (string.Equals(
                        plan.Ridges[index].StableId,
                        stableId,
                        StringComparison.Ordinal))
                {
                    found = true;
                    return plan.Ridges[index];
                }
            }

            found = false;
            return default;
        }

        private static void ValidateLandmarkSeparation(
            MountainRoadTerminalPlan terminal)
        {
            MountainRoadCafePlan cafe = terminal.Cafe;
            MountainRoadTerminalRect station = terminal.Cableway.StationArea;
            int[] stationCornerOrder = { 0, 1, 3, 2 };
            var stationCorners = new Vector2[4];
            for (int index = 0; index < stationCorners.Length; index++)
            {
                stationCorners[index] = ToXZ(station.GetCorner(
                    stationCornerOrder[index]));
                if (ContainsPolygon(cafe.FootprintXZ, stationCorners[index]))
                {
                    throw new InvalidOperationException(
                        "The cafe and cable station overlap.");
                }
            }

            for (int cafeIndex = 0;
                 cafeIndex < cafe.FootprintXZ.Count;
                 cafeIndex++)
            {
                Vector2 cafePoint = cafe.FootprintXZ[cafeIndex];
                Vector3 world = new Vector3(
                    cafePoint.x,
                    station.Center.y,
                    cafePoint.y);
                if (station.ContainsXZ(world))
                {
                    throw new InvalidOperationException(
                        "The cafe and cable station overlap.");
                }

                Vector2 cafeNext = cafe.FootprintXZ[
                    (cafeIndex + 1) % cafe.FootprintXZ.Count];
                for (int stationIndex = 0;
                     stationIndex < stationCorners.Length;
                     stationIndex++)
                {
                    Vector2 stationNext = stationCorners[
                        (stationIndex + 1) % stationCorners.Length];
                    if (SegmentsIntersect(
                            cafePoint,
                            cafeNext,
                            stationCorners[stationIndex],
                            stationNext))
                    {
                        throw new InvalidOperationException(
                            "The cafe and cable station overlap.");
                    }
                }
            }
        }

        private static void ValidateLandmarks(
            MountainRoadPlan plan,
            IReadOnlyList<MountainRoadTerminalLandmark> landmarks)
        {
            if (landmarks.Count != 3)
            {
                throw new InvalidOperationException(
                    "The mountain map needs the cafe, the cableway " +
                    "and the brink.");
            }

            var kinds = new HashSet<MountainRoadTerminalLandmarkKind>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < landmarks.Count; index++)
            {
                MountainRoadTerminalLandmark landmark = landmarks[index];
                if (string.IsNullOrWhiteSpace(landmark.StableId) ||
                    string.IsNullOrWhiteSpace(landmark.LocalizationKey) ||
                    !ids.Add(landmark.StableId) ||
                    !kinds.Add(landmark.Kind) ||
                    !plan.Plateau.Contains(ToXZ(landmark.Position)))
                {
                    throw new InvalidOperationException(
                        "Terminal landmarks must be unique and lie on the " +
                        "plateau map.");
                }
            }
        }

        private static void ValidateTerminalExclusions(
            MountainRoadPlan plan,
            MountainRoadCablewayPlan cableway)
        {
            for (int index = 0; index < plan.Forest.Count; index++)
            {
                MountainRoadForestDescriptor tree = plan.Forest[index];
                Vector2 point = ToXZ(tree.Position);
                if (plan.Plateau.BoundsXZ.Contains(point) ||
                    cableway.ContainsClearanceXZ(
                        point,
                        tree.CrownRadius + 0.8f))
                {
                    throw new InvalidOperationException(
                        $"{tree.StableId} enters the terminal or cable " +
                        "clearance.");
                }
            }
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        private static void ValidatePolygonOutsideCircle(
            IReadOnlyList<Vector2> polygon,
            Vector2 center,
            float radius,
            string message)
        {
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 first = polygon[index];
                Vector2 second = polygon[(index + 1) % polygon.Count];
                if (PointSegmentDistance(center, first, second) < radius)
                {
                    throw new InvalidOperationException(message);
                }
            }
        }

        private static float DistanceToPolygonEdge(
            IReadOnlyList<Vector2> polygon,
            Vector2 point)
        {
            float distance = float.PositiveInfinity;
            for (int index = 0; index < polygon.Count; index++)
            {
                distance = Mathf.Min(
                    distance,
                    PointSegmentDistance(
                        point,
                        polygon[index],
                        polygon[(index + 1) % polygon.Count]));
            }

            return distance;
        }

        private static float PointSegmentDistance(
            Vector2 point,
            Vector2 first,
            Vector2 second)
        {
            Vector2 segment = second - first;
            float denominator = segment.sqrMagnitude;
            float amount = denominator <= 0.000001f
                ? 0f
                : Mathf.Clamp01(Vector2.Dot(point - first, segment) /
                                denominator);
            return Vector2.Distance(
                point,
                Vector2.Lerp(first, second, amount));
        }

        private static bool ContainsPolygon(
            IReadOnlyList<Vector2> polygon,
            Vector2 point)
        {
            bool inside = false;
            for (int first = 0, second = polygon.Count - 1;
                 first < polygon.Count;
                 second = first++)
            {
                Vector2 a = polygon[first];
                Vector2 b = polygon[second];
                bool crosses = (a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) *
                    (point.y - a.y) /
                    ((b.y - a.y) + Mathf.Epsilon) + a.x;
                if (crosses)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static bool SegmentsIntersect(
            Vector2 firstA,
            Vector2 firstB,
            Vector2 secondA,
            Vector2 secondB)
        {
            float orientation1 = Cross(
                firstB - firstA,
                secondA - firstA);
            float orientation2 = Cross(
                firstB - firstA,
                secondB - firstA);
            float orientation3 = Cross(
                secondB - secondA,
                firstA - secondA);
            float orientation4 = Cross(
                secondB - secondA,
                firstB - secondA);
            const float epsilon = 0.00001f;
            return orientation1 * orientation2 < -epsilon &&
                   orientation3 * orientation4 < -epsilon;
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private static void RequireFinite(Vector3 value, string label)
        {
            if (!IsFinite(value.x) ||
                !IsFinite(value.y) ||
                !IsFinite(value.z))
            {
                throw new InvalidOperationException(label + " must be finite.");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
