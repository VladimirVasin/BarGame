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
            if ((!plan.IsCorrupted && !walkable.Contains(
                    plan.StreetSpawn,
                    PlayerRadius)) ||
                !walkable.Contains(
                    plan.ApartmentSpawn,
                    PlayerRadius))
            {
                throw new InvalidOperationException(
                    "Stairwell spawns must be safely walkable.");
            }

            if (!plan.IsCorrupted)
                ValidateTrigger(plan, plan.StreetExitPosition, plan.StreetExitTriggerSize, "street exit");
            ValidateTrigger(
                plan,
                plan.ApartmentEntrancePosition,
                plan.ApartmentEntranceTriggerSize,
                "apartment entrance");
            if (!plan.IsCorrupted)
                ValidateDoorDock(plan.StreetLobbyBounds, plan.StreetDoorActionRoot, plan.StreetElevation);
            ValidateDoorDock(plan.ApartmentLandingBounds, plan.ApartmentDoorActionRoot, plan.ApartmentElevation);
            ValidateUpperBlocker(plan);
            if (plan.IsCorrupted)
            {
                ValidateConnection(plan.UpperFlightBounds, plan.UpperLandingBounds,
                    "opened upper flight to placeholder landing");
                ValidateLabyrinth(plan);
                Bounds wall = plan.DescentWallBounds;
                if (wall.min.x > plan.ApartmentFlightBounds.xMin ||
                    wall.max.x < plan.ApartmentFlightBounds.xMax ||
                    wall.min.y > plan.ApartmentElevation + Epsilon ||
                    wall.max.y < plan.RoomHeight - Epsilon ||
                    wall.min.z > plan.ApartmentFlight.Start.y - plan.ApartmentFlight.RunLength + .03f ||
                    plan.RoomHeight - plan.UpperFlight.TopElevation < 2.2f)
                    throw new InvalidOperationException("Corrupted stairwell needs a sealed descent and standing upper route.");
            }
        }

        private static void ValidateLabyrinth(StairwellLayoutPlan plan)
        {
            StairwellLabyrinthPlan labyrinth = plan.Labyrinth;
            if (labyrinth == null || labyrinth.Flights.Count != 3 || labyrinth.Branches.Count != 9)
                throw new InvalidOperationException("The labyrinth needs three main rises and two/three/four explorable branches.");
            for (int i = 0; i < labyrinth.Flights.Count; i++)
            {
                StairwellFlightPlan flight = labyrinth.Flights[i];
                ValidateFlight(flight, flight.BaseElevation, flight.TopElevation);
                if (i > 0 && Mathf.Abs(labyrinth.Flights[i - 1].TopElevation - flight.BaseElevation) > Epsilon)
                    throw new InvalidOperationException("The three principal rises must share continuous storeys.");
            }
            if (Mathf.Abs(labyrinth.Flights[0].BaseElevation - plan.UpperFlight.TopElevation) > Epsilon)
                throw new InvalidOperationException("The labyrinth must start on the existing upper landing.");
            var area = new StairwellWalkableArea(plan);
            ValidateLabyrinthPath(area, labyrinth.RouteWaypoints, "main route");
            float mainLength = 0f;
            for (int i = 1; i < labyrinth.RouteWaypoints.Count; i++)
                mainLength += Vector3.Distance(labyrinth.RouteWaypoints[i - 1], labyrinth.RouteWaypoints[i]);
            if (mainLength < 160f || mainLength > 210f)
                throw new InvalidOperationException("The enlarged main route must be approximately 160–200 metres.");
            int[] branchesByStage = new int[3];
            foreach (StairwellLabyrinthBranchPlan branch in labyrinth.Branches)
            {
                if (branch.Stage < 1 || branch.Stage > 3)
                    throw new InvalidOperationException("A branch must belong to a labyrinth stage.");
                branchesByStage[branch.Stage - 1]++;
                ValidateLabyrinthPath(area, branch.Waypoints, branch.Id);
                if (!IsMainWaypoint(labyrinth, branch.Waypoints[0]) ||
                    (branch.IsLoop && !IsMainWaypoint(labyrinth, branch.Waypoints[branch.Waypoints.Count - 1])))
                    throw new InvalidOperationException("Every branch must join the main route, including its loop return: " + branch.Id);
            }
            for (int stage = 0; stage < 3; stage++)
                if (branchesByStage[stage] != stage + 2)
                    throw new InvalidOperationException("Each successive stage needs another false route.");
            if (!area.TryGetHeight(labyrinth.DoorActionRoot, out float doorFloor, PlayerRadius) ||
                Mathf.Abs(doorFloor + PlayerFactory.GroundedRootOffset - labyrinth.DoorActionRoot.y) > Epsilon)
                throw new InvalidOperationException("The warehouse gesture must stay supported on its own storey.");
            bool[] reached = new bool[labyrinth.Supports.Count];
            var pending = new System.Collections.Generic.Queue<int>();
            if (reached.Length == 0) throw new InvalidOperationException("The labyrinth has no supporting floors.");
            reached[0] = true; pending.Enqueue(0);
            while (pending.Count > 0)
            {
                int index = pending.Dequeue();
                StairwellLabyrinthSupportPlan support = labyrinth.Supports[index];
                if (support.Width < MinimumFlightWidth || support.Stage < 1 || support.Stage > 3 ||
                    !IsFinite(support.Start.y) || !IsFinite(support.End.y))
                    throw new InvalidOperationException("Invalid labyrinth support: " + support.Id);
                foreach (int neighbour in support.Connections)
                {
                    if (neighbour < 0 || neighbour >= reached.Length)
                        throw new InvalidOperationException("Invalid labyrinth connection: " + support.Id);
                    if (reached[neighbour]) continue;
                    reached[neighbour] = true; pending.Enqueue(neighbour);
                }
            }
            foreach (bool connected in reached)
                if (!connected) throw new InvalidOperationException("A playable labyrinth branch is disconnected.");
        }

        private static bool IsMainWaypoint(StairwellLabyrinthPlan labyrinth, Vector3 point)
        {
            foreach (Vector3 waypoint in labyrinth.RouteWaypoints)
                if ((waypoint - point).sqrMagnitude < .01f) return true;
            return false;
        }

        private static void ValidateLabyrinthPath(StairwellWalkableArea area,
            System.Collections.Generic.IReadOnlyList<Vector3> path, string label)
        {
            if (path.Count < 2) throw new InvalidOperationException("Empty labyrinth route: " + label);
            for (int index = 0; index < path.Count; index++)
            {
                Vector3 start = index == 0 ? path[index] : path[index - 1];
                Vector3 end = path[index];
                int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / .25f));
                for (int sample = 0; sample <= samples; sample++)
                {
                    Vector3 point = Vector3.Lerp(start, end, sample / (float)samples);
                    if (!area.Contains(point, PlayerRadius))
                        throw new InvalidOperationException("Labyrinth path lacks capsule support/headroom: " + label + " at " + point);
                }
            }
        }

        private static void ValidateDoorDock(Rect floor, Vector3 dock, float elevation)
        {
            Rect safe = Rect.MinMaxRect(floor.xMin + PlayerRadius, floor.yMin + PlayerRadius,
                floor.xMax - PlayerRadius, floor.yMax - PlayerRadius);
            if (!safe.Contains(new Vector2(dock.x, dock.z)) ||
                Mathf.Abs(dock.y - elevation - PlayerFactory.GroundedRootOffset) > Epsilon)
                throw new InvalidOperationException("The source door gesture needs a grounded, capsule-clear dock on its own landing.");
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
