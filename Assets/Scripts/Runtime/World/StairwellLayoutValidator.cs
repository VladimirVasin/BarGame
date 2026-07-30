using System;
using UnityEngine;

namespace BarPromenade
{
    public static class StairwellLayoutValidator
    {
        public const float PlayerRadius = 0.32f;
        public const float MaximumStepRise = 0.28f;
        public const float MinimumFlightWidth = 1.20f;
        private const float Epsilon = 0.001f;

        public static void ValidateOrThrow(
            StairwellLayoutPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!IsPositive(plan.RoomSize.x) ||
                !IsPositive(plan.RoomSize.y) ||
                !IsPositive(plan.RoomHeight))
            {
                throw new InvalidOperationException(
                    "Stairwell room dimensions must be positive.");
            }

            ValidateFlight(
                plan.LowerFlight,
                plan.StreetElevation,
                plan.MiddleElevation);
            ValidateFlight(
                plan.ApartmentFlight,
                plan.MiddleElevation,
                plan.ApartmentElevation);
            ValidateFlight(
                plan.UpperFlight,
                plan.ApartmentElevation,
                plan.ApartmentElevation +
                (StairwellLayoutPlanner.StepsPerFlight *
                 StairwellLayoutPlanner.StepRise));

            ValidateConnection(
                plan.StreetLobbyBounds,
                plan.LowerFlightBounds,
                "street lobby to lower flight");
            ValidateConnection(
                plan.LowerFlightBounds,
                plan.MiddleLandingBounds,
                "lower flight to middle landing");
            ValidateConnection(
                plan.MiddleLandingBounds,
                plan.ApartmentFlightBounds,
                "middle landing to apartment flight");
            ValidateConnection(
                plan.ApartmentFlightBounds,
                plan.ApartmentLandingBounds,
                "apartment flight to apartment landing");
            ValidateConnection(
                plan.ApartmentLandingBounds,
                plan.UpperFlightBounds,
                "apartment landing to upper flight");

            var walkable =
                new RoadWalkableArea(plan.WalkableRectangles);
            if (!walkable.Contains(
                    plan.StreetSpawn,
                    PlayerRadius) ||
                !walkable.Contains(
                    plan.ApartmentSpawn,
                    PlayerRadius))
            {
                throw new InvalidOperationException(
                    "Stairwell spawns must be safely walkable.");
            }

            ValidateTrigger(
                plan,
                plan.StreetExitPosition,
                plan.StreetExitTriggerSize,
                "street exit");
            ValidateTrigger(
                plan,
                plan.ApartmentEntrancePosition,
                plan.ApartmentEntranceTriggerSize,
                "apartment entrance");
            ValidateUpperBlocker(plan);
        }

        private static void ValidateFlight(
            StairwellFlightPlan flight,
            float expectedBase,
            float expectedTop)
        {
            if (flight.StepCount <= 0 ||
                !IsPositive(flight.StepRise) ||
                flight.StepRise > MaximumStepRise + Epsilon ||
                !IsPositive(flight.StepDepth) ||
                flight.Width < MinimumFlightWidth ||
                !IsFinite(flight.BaseElevation))
            {
                throw new InvalidOperationException(
                    $"Stair flight '{flight.Id}' is not traversable.");
            }

            if (flight.StepRise >
                flight.StepDepth + Epsilon)
            {
                throw new InvalidOperationException(
                    $"Stair flight '{flight.Id}' exceeds a 45 degree slope.");
            }

            if (Mathf.Abs(
                    flight.BaseElevation - expectedBase) > Epsilon ||
                Mathf.Abs(
                    flight.TopElevation - expectedTop) > Epsilon)
            {
                throw new InvalidOperationException(
                    $"Stair flight '{flight.Id}' has inconsistent elevations.");
            }
        }

        private static void ValidateConnection(
            Rect first,
            Rect second,
            string label)
        {
            Rect expanded = new Rect(
                first.xMin - Epsilon,
                first.yMin - Epsilon,
                first.width + (Epsilon * 2f),
                first.height + (Epsilon * 2f));
            if (!expanded.Overlaps(second, true))
            {
                throw new InvalidOperationException(
                    $"Stairwell path is disconnected at {label}.");
            }
        }

        private static void ValidateTrigger(
            StairwellLayoutPlan plan,
            Vector3 position,
            Vector3 size,
            string label)
        {
            if (!IsFinite(position) ||
                !IsPositive(size.x) ||
                !IsPositive(size.y) ||
                !IsPositive(size.z))
            {
                throw new InvalidOperationException(
                    $"The {label} trigger is invalid.");
            }

            float halfWidth = plan.RoomSize.x * 0.5f;
            float halfDepth = plan.RoomSize.y * 0.5f;
            if (Mathf.Abs(position.x) > halfWidth + Epsilon ||
                Mathf.Abs(position.z) > halfDepth + Epsilon ||
                position.y < 0f ||
                position.y > plan.RoomHeight)
            {
                throw new InvalidOperationException(
                    $"The {label} trigger is outside the room.");
            }
        }

        private static void ValidateUpperBlocker(
            StairwellLayoutPlan plan)
        {
            Bounds blocker = plan.UpperBlockerBounds;
            Rect flight = plan.UpperFlightBounds;
            if (!IsFinite(blocker.center) ||
                !IsPositive(blocker.size.x) ||
                !IsPositive(blocker.size.y) ||
                !IsPositive(blocker.size.z) ||
                blocker.min.x > flight.xMin + Epsilon ||
                blocker.max.x < flight.xMax - Epsilon ||
                blocker.min.y > plan.ApartmentElevation + Epsilon ||
                blocker.max.y <
                plan.ApartmentElevation + 1.70f)
            {
                throw new InvalidOperationException(
                    "The upper-flight blocker must seal the whole passage.");
            }
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
