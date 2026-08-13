using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityExteriorStairPlanner
    {
        public const float DefaultStepRise = 0.16f;
        public const float DefaultTreadDepth = 0.32f;
        public const float DefaultLandingLength = 1.5f;
        public const float DefaultLandingThickness = 0.18f;
        public const float DefaultRailHeight = 1.05f;
        public const float DefaultRailThickness = 0.10f;
        public const float DefaultRetainingWallThickness = 0.24f;

        /// <summary>
        /// Creates one straight bottom-to-top flight with level approach
        /// landings. All coordinates are local to the future stair root.
        /// </summary>
        public static CityExteriorStairPlan CreateStraightFlight(
            string id,
            Vector3 flightStart,
            Vector3 ascentDirection,
            float width,
            int stepCount,
            float stepRise = DefaultStepRise,
            float treadDepth = DefaultTreadDepth,
            float landingLength = DefaultLandingLength,
            CityExteriorStairDropSide dropSides =
                CityExteriorStairDropSide.Both,
            bool includeRetainingWalls = true)
        {
            Vector3 direction = NormalizePlanar(ascentDirection);
            string flightId = $"{id}:flight:01";
            var flight = new CityExteriorStairFlightDescriptor(
                flightId,
                flightStart,
                direction,
                width,
                stepCount,
                stepRise,
                treadDepth,
                dropSides);
            var flights = new List<CityExteriorStairFlightDescriptor>
            {
                flight
            };

            string lowerId = $"{id}:landing:lower";
            string upperId = $"{id}:landing:upper";
            var lower = new CityExteriorStairLandingDescriptor(
                lowerId,
                flightId,
                CityExteriorStairLandingKind.Lower,
                flight.Start - (direction * (landingLength * 0.5f)),
                direction,
                width,
                landingLength,
                flight.BaseElevation,
                DefaultLandingThickness,
                dropSides);
            var upper = new CityExteriorStairLandingDescriptor(
                upperId,
                flightId,
                CityExteriorStairLandingKind.Upper,
                flight.End + (direction * (landingLength * 0.5f)),
                direction,
                width,
                landingLength,
                flight.TopElevation,
                DefaultLandingThickness,
                dropSides);
            var landings = new List<CityExteriorStairLandingDescriptor>
            {
                lower,
                upper
            };

            var rails = new List<CityExteriorStairRailDescriptor>(6);
            AddSideGeometry(
                id,
                CityExteriorStairDropSide.Left,
                dropSides,
                flight,
                lower,
                upper,
                includeRetainingWalls,
                rails,
                out CityExteriorStairRetainingWallDescriptor leftWall,
                out bool hasLeftWall);
            AddSideGeometry(
                id,
                CityExteriorStairDropSide.Right,
                dropSides,
                flight,
                lower,
                upper,
                includeRetainingWalls,
                rails,
                out CityExteriorStairRetainingWallDescriptor rightWall,
                out bool hasRightWall);

            var retainingWalls =
                new List<CityExteriorStairRetainingWallDescriptor>(2);
            if (hasLeftWall)
            {
                retainingWalls.Add(leftWall);
            }

            if (hasRightWall)
            {
                retainingWalls.Add(rightWall);
            }

            var plan = new CityExteriorStairPlan(
                id,
                flights,
                landings,
                rails,
                retainingWalls);
            CityExteriorStairValidator.ValidateOrThrow(plan);
            return plan;
        }

        private static void AddSideGeometry(
            string planId,
            CityExteriorStairDropSide side,
            CityExteriorStairDropSide requestedSides,
            CityExteriorStairFlightDescriptor flight,
            CityExteriorStairLandingDescriptor lower,
            CityExteriorStairLandingDescriptor upper,
            bool includeRetainingWalls,
            ICollection<CityExteriorStairRailDescriptor> rails,
            out CityExteriorStairRetainingWallDescriptor retainingWall,
            out bool hasRetainingWall)
        {
            retainingWall = default;
            hasRetainingWall = false;
            if ((requestedSides & side) == 0)
            {
                return;
            }

            string sideId = side == CityExteriorStairDropSide.Left
                ? "left"
                : "right";
            rails.Add(CreateRail(
                $"{planId}:rail:lower:{sideId}",
                lower.Id,
                CityExteriorStairRailOwnerKind.Landing,
                side,
                lower.GetSidePoint(side, false),
                lower.GetSidePoint(side, true)));
            rails.Add(CreateRail(
                $"{planId}:rail:flight:{sideId}",
                flight.Id,
                CityExteriorStairRailOwnerKind.Flight,
                side,
                flight.GetSidePoint(side, false),
                flight.GetSidePoint(side, true)));
            rails.Add(CreateRail(
                $"{planId}:rail:upper:{sideId}",
                upper.Id,
                CityExteriorStairRailOwnerKind.Landing,
                side,
                upper.GetSidePoint(side, false),
                upper.GetSidePoint(side, true)));

            if (!includeRetainingWalls)
            {
                return;
            }

            retainingWall =
                new CityExteriorStairRetainingWallDescriptor(
                    $"{planId}:retaining:upper:{sideId}",
                    upper.GetSidePoint(side, false),
                    upper.GetSidePoint(side, true),
                    flight.BaseElevation,
                    DefaultRetainingWallThickness);
            hasRetainingWall = true;
        }

        private static CityExteriorStairRailDescriptor CreateRail(
            string id,
            string ownerId,
            CityExteriorStairRailOwnerKind ownerKind,
            CityExteriorStairDropSide side,
            Vector3 surfaceStart,
            Vector3 surfaceEnd)
        {
            return new CityExteriorStairRailDescriptor(
                id,
                ownerId,
                ownerKind,
                side,
                surfaceStart,
                surfaceEnd,
                DefaultRailHeight,
                DefaultRailThickness);
        }

        private static Vector3 NormalizePlanar(Vector3 direction)
        {
            direction.y = 0f;
            if (!IsFinite(direction) ||
                direction.sqrMagnitude <= 0.000001f)
            {
                return Vector3.zero;
            }

            return direction.normalized;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
