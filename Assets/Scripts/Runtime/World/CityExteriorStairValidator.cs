using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityExteriorStairValidator
    {
        public const int MinimumStepCount = 6;
        public const int MaximumStepCount = 12;
        public const float MinimumStepRise = 0.15f;
        public const float MaximumStepRise = 0.17f;
        public const float MinimumTreadDepth = 0.30f;
        public const float MaximumTreadDepth = 0.34f;
        public const float MinimumLandingLength = 1.5f;
        public const float MinimumFlightWidth = 1.6f;
        public const float MinimumRailHeight = 0.90f;

        private const float GeometryTolerance = 0.001f;
        private const CityExteriorStairDropSide SupportedDropSides =
            CityExteriorStairDropSide.Both;

        public static void ValidateOrThrow(CityExteriorStairPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (string.IsNullOrWhiteSpace(plan.Id))
            {
                throw new InvalidOperationException(
                    "An exterior stair plan requires a stable ID.");
            }

            if (plan.Flights.Count == 0)
            {
                throw new InvalidOperationException(
                    "An exterior stair plan requires at least one flight.");
            }

            ValidateUniqueIds(plan);
            for (int index = 0; index < plan.Flights.Count; index++)
            {
                ValidateFlight(plan, plan.Flights[index]);
            }

            for (int index = 0; index < plan.Landings.Count; index++)
            {
                ValidateLanding(plan, plan.Landings[index]);
            }

            for (int index = 0; index < plan.Rails.Count; index++)
            {
                ValidateRail(plan, plan.Rails[index]);
            }

            for (int index = 0;
                 index < plan.RetainingWalls.Count;
                 index++)
            {
                ValidateRetainingWall(plan.RetainingWalls[index]);
            }

            ValidateRequiredRails(plan);
        }

        private static void ValidateFlight(
            CityExteriorStairPlan plan,
            CityExteriorStairFlightDescriptor flight)
        {
            if (string.IsNullOrWhiteSpace(flight.Id) ||
                !IsFinite(flight.Start) ||
                !IsUnitPlanar(flight.Direction) ||
                !IsFinite(flight.Width) ||
                flight.Width < MinimumFlightWidth ||
                flight.StepCount < MinimumStepCount ||
                flight.StepCount > MaximumStepCount ||
                !InRange(
                    flight.StepRise,
                    MinimumStepRise,
                    MaximumStepRise) ||
                !InRange(
                    flight.TreadDepth,
                    MinimumTreadDepth,
                    MaximumTreadDepth) ||
                !HasOnlySupportedDropSides(flight.DropSides))
            {
                throw new InvalidOperationException(
                    $"Exterior stair flight '{flight.Id}' is invalid.");
            }

            int lowerCount = 0;
            int upperCount = 0;
            for (int index = 0; index < plan.Landings.Count; index++)
            {
                CityExteriorStairLandingDescriptor landing =
                    plan.Landings[index];
                if (!string.Equals(
                        landing.ConnectedFlightId,
                        flight.Id,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (landing.Kind == CityExteriorStairLandingKind.Lower)
                {
                    lowerCount++;
                }
                else if (landing.Kind ==
                         CityExteriorStairLandingKind.Upper)
                {
                    upperCount++;
                }
            }

            if (lowerCount != 1 || upperCount != 1)
            {
                throw new InvalidOperationException(
                    $"Exterior stair flight '{flight.Id}' requires exactly " +
                    "one lower and one upper landing.");
            }
        }

        private static void ValidateLanding(
            CityExteriorStairPlan plan,
            CityExteriorStairLandingDescriptor landing)
        {
            if (string.IsNullOrWhiteSpace(landing.Id) ||
                string.IsNullOrWhiteSpace(landing.ConnectedFlightId) ||
                !IsFinite(landing.Center) ||
                !IsUnitPlanar(landing.Direction) ||
                !IsFinite(landing.Width) ||
                !IsFinite(landing.Length) ||
                !IsFinite(landing.Elevation) ||
                !IsPositive(landing.Thickness) ||
                landing.Length < MinimumLandingLength ||
                !HasOnlySupportedDropSides(landing.DropSides))
            {
                throw new InvalidOperationException(
                    $"Exterior stair landing '{landing.Id}' is invalid.");
            }

            if (!TryFindFlight(
                    plan,
                    landing.ConnectedFlightId,
                    out CityExteriorStairFlightDescriptor flight))
            {
                throw new InvalidOperationException(
                    $"Exterior stair landing '{landing.Id}' references " +
                    "an unknown flight.");
            }

            if (landing.Width < flight.Width - GeometryTolerance ||
                Vector3.Dot(
                    landing.Direction,
                    flight.Direction) < 1f - GeometryTolerance)
            {
                throw new InvalidOperationException(
                    $"Exterior stair landing '{landing.Id}' does not match " +
                    $"flight '{flight.Id}'.");
            }

            Vector3 expectedSeam;
            Vector3 actualSeam;
            float expectedElevation;
            if (landing.Kind == CityExteriorStairLandingKind.Lower)
            {
                expectedSeam = flight.Start;
                actualSeam = landing.EndEdgeCenter;
                expectedElevation = flight.BaseElevation;
            }
            else
            {
                expectedSeam = flight.End;
                actualSeam = landing.StartEdgeCenter;
                expectedElevation = flight.TopElevation;
            }

            if (!Approximately(actualSeam, expectedSeam) ||
                Mathf.Abs(landing.Elevation - expectedElevation) >
                GeometryTolerance ||
                Mathf.Abs(landing.Center.y - landing.Elevation) >
                GeometryTolerance)
            {
                throw new InvalidOperationException(
                    $"Exterior stair landing '{landing.Id}' is disconnected " +
                    $"from flight '{flight.Id}'.");
            }
        }

        private static void ValidateRail(
            CityExteriorStairPlan plan,
            CityExteriorStairRailDescriptor rail)
        {
            if (string.IsNullOrWhiteSpace(rail.Id) ||
                string.IsNullOrWhiteSpace(rail.OwnerId) ||
                !IsSingleSide(rail.Side) ||
                !IsFinite(rail.SurfaceStart) ||
                !IsFinite(rail.SurfaceEnd) ||
                !IsPositive(rail.Thickness) ||
                !IsFinite(rail.Height) ||
                rail.Height < MinimumRailHeight ||
                Vector3.Distance(
                    rail.SurfaceStart,
                    rail.SurfaceEnd) <= GeometryTolerance)
            {
                throw new InvalidOperationException(
                    $"Exterior stair rail '{rail.Id}' is invalid.");
            }

            if (rail.OwnerKind == CityExteriorStairRailOwnerKind.Flight)
            {
                if (!TryFindFlight(
                        plan,
                        rail.OwnerId,
                        out CityExteriorStairFlightDescriptor flight) ||
                    !Approximately(
                        rail.SurfaceStart,
                        flight.GetSidePoint(rail.Side, false)) ||
                    !Approximately(
                        rail.SurfaceEnd,
                        flight.GetSidePoint(rail.Side, true)))
                {
                    throw new InvalidOperationException(
                        $"Exterior stair rail '{rail.Id}' does not span its " +
                        "flight edge.");
                }

                return;
            }

            if (!TryFindLanding(
                    plan,
                    rail.OwnerId,
                    out CityExteriorStairLandingDescriptor landing) ||
                !Approximately(
                    rail.SurfaceStart,
                    landing.GetSidePoint(rail.Side, false)) ||
                !Approximately(
                    rail.SurfaceEnd,
                    landing.GetSidePoint(rail.Side, true)))
            {
                throw new InvalidOperationException(
                    $"Exterior stair rail '{rail.Id}' does not span its " +
                    "landing edge.");
            }
        }

        private static void ValidateRetainingWall(
            CityExteriorStairRetainingWallDescriptor wall)
        {
            Vector3 run = wall.TopEnd - wall.TopStart;
            if (string.IsNullOrWhiteSpace(wall.Id) ||
                !IsFinite(wall.TopStart) ||
                !IsFinite(wall.TopEnd) ||
                !IsFinite(wall.BottomElevation) ||
                !IsPositive(wall.Thickness) ||
                Mathf.Abs(run.y) > GeometryTolerance ||
                new Vector2(run.x, run.z).magnitude <= GeometryTolerance ||
                wall.Height <= GeometryTolerance)
            {
                throw new InvalidOperationException(
                    $"Exterior retaining wall '{wall.Id}' is invalid.");
            }
        }

        private static void ValidateRequiredRails(
            CityExteriorStairPlan plan)
        {
            for (int index = 0; index < plan.Flights.Count; index++)
            {
                CityExteriorStairFlightDescriptor flight =
                    plan.Flights[index];
                ValidateOwnerRails(
                    plan,
                    flight.Id,
                    CityExteriorStairRailOwnerKind.Flight,
                    flight.DropSides);
            }

            for (int index = 0; index < plan.Landings.Count; index++)
            {
                CityExteriorStairLandingDescriptor landing =
                    plan.Landings[index];
                ValidateOwnerRails(
                    plan,
                    landing.Id,
                    CityExteriorStairRailOwnerKind.Landing,
                    landing.DropSides);
            }
        }

        private static void ValidateOwnerRails(
            CityExteriorStairPlan plan,
            string ownerId,
            CityExteriorStairRailOwnerKind ownerKind,
            CityExteriorStairDropSide dropSides)
        {
            ValidateOwnerSideRail(
                plan,
                ownerId,
                ownerKind,
                dropSides,
                CityExteriorStairDropSide.Left);
            ValidateOwnerSideRail(
                plan,
                ownerId,
                ownerKind,
                dropSides,
                CityExteriorStairDropSide.Right);
        }

        private static void ValidateOwnerSideRail(
            CityExteriorStairPlan plan,
            string ownerId,
            CityExteriorStairRailOwnerKind ownerKind,
            CityExteriorStairDropSide dropSides,
            CityExteriorStairDropSide side)
        {
            if ((dropSides & side) == 0)
            {
                return;
            }

            int count = 0;
            for (int index = 0; index < plan.Rails.Count; index++)
            {
                CityExteriorStairRailDescriptor rail = plan.Rails[index];
                if (rail.OwnerKind == ownerKind &&
                    rail.Side == side &&
                    string.Equals(
                        rail.OwnerId,
                        ownerId,
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }

            if (count != 1)
            {
                throw new InvalidOperationException(
                    $"Exposed {side} edge on '{ownerId}' requires exactly " +
                    "one guard rail.");
            }
        }

        private static void ValidateUniqueIds(CityExteriorStairPlan plan)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            AddIds(plan.Flights, item => item.Id, ids, "flight");
            AddIds(plan.Landings, item => item.Id, ids, "landing");
            AddIds(plan.Rails, item => item.Id, ids, "rail");
            AddIds(
                plan.RetainingWalls,
                item => item.Id,
                ids,
                "retaining wall");
        }

        private static void AddIds<T>(
            IReadOnlyList<T> source,
            Func<T, string> getId,
            ISet<string> ids,
            string label)
        {
            for (int index = 0; index < source.Count; index++)
            {
                string id = getId(source[index]);
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                {
                    throw new InvalidOperationException(
                        $"Exterior stair {label} IDs must be non-empty " +
                        "and globally unique.");
                }
            }
        }

        private static bool TryFindFlight(
            CityExteriorStairPlan plan,
            string id,
            out CityExteriorStairFlightDescriptor flight)
        {
            for (int index = 0; index < plan.Flights.Count; index++)
            {
                CityExteriorStairFlightDescriptor candidate =
                    plan.Flights[index];
                if (string.Equals(
                        candidate.Id,
                        id,
                        StringComparison.Ordinal))
                {
                    flight = candidate;
                    return true;
                }
            }

            flight = default;
            return false;
        }

        private static bool TryFindLanding(
            CityExteriorStairPlan plan,
            string id,
            out CityExteriorStairLandingDescriptor landing)
        {
            for (int index = 0; index < plan.Landings.Count; index++)
            {
                CityExteriorStairLandingDescriptor candidate =
                    plan.Landings[index];
                if (string.Equals(
                        candidate.Id,
                        id,
                        StringComparison.Ordinal))
                {
                    landing = candidate;
                    return true;
                }
            }

            landing = default;
            return false;
        }

        private static bool HasOnlySupportedDropSides(
            CityExteriorStairDropSide sides)
        {
            return (sides & ~SupportedDropSides) == 0;
        }

        private static bool IsSingleSide(CityExteriorStairDropSide side)
        {
            return side == CityExteriorStairDropSide.Left ||
                   side == CityExteriorStairDropSide.Right;
        }

        private static bool IsUnitPlanar(Vector3 value)
        {
            return IsFinite(value) &&
                   Mathf.Abs(value.y) <= GeometryTolerance &&
                   Mathf.Abs(value.magnitude - 1f) <= GeometryTolerance;
        }

        private static bool Approximately(Vector3 first, Vector3 second)
        {
            return Vector3.Distance(first, second) <= GeometryTolerance;
        }

        private static bool InRange(float value, float minimum, float maximum)
        {
            return IsFinite(value) &&
                   value >= minimum &&
                   value <= maximum;
        }

        private static bool IsPositive(float value)
        {
            return IsFinite(value) && value > 0f;
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
