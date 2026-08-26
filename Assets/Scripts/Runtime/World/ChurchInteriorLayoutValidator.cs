using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class ChurchInteriorLayoutValidator
    {
        public const int RequiredZoneCount = 4;
        public const int RequiredPathCount = 5;
        public const int RequiredFixtureCount = 31;
        public const int RequiredPierCount = 4;
        public const int RequiredPewCount = 12;
        public const int RequiredConfessionalCount = 2;
        public const int RequiredVotiveStandCount = 2;
        public const int RequiredChoirLoftSupportCount = 4;
        public const float MinimumRouteClearance = 2.0f;
        public const float PlayerRouteClearHeight = 2.2f;

        private const float Tolerance = 0.001f;

        public static void ValidateOrThrow(
            ChurchInteriorLayoutPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!IsPositive(plan.RoomSize) ||
                !IsPositiveFinite(plan.RoomHeight) ||
                !IsPositiveFinite(plan.WallThickness) ||
                !IsPositive(plan.RoomBounds.size) ||
                !IsFinite(plan.ModelLocalBounds.center) ||
                !IsPositive(plan.ModelLocalBounds.size) ||
                !IsPositive(plan.WalkableBounds.size) ||
                !Contains(plan.RoomBounds, plan.WalkableBounds) ||
                !Contains(plan.WalkableBounds, plan.PlayerSpawn) ||
                !IsFinite(plan.ExitPosition) ||
                !IsPositive(plan.ExitTriggerSize) ||
                string.IsNullOrWhiteSpace(plan.ModelResourcePath))
            {
                throw new InvalidOperationException(
                    "The church requires finite room, walkable, spawn, " +
                    "exit and model data.");
            }

            if (plan.RoomHeight <
                    ChurchInteriorLayoutPlanner.ModelMaximumHeight ||
                plan.RoomHeight >
                    ChurchInteriorLayoutPlanner.ModelMaximumHeight + 0.5f ||
                Mathf.Abs(
                    plan.ModelLocalBounds.max.y -
                    ChurchInteriorLayoutPlanner.ModelMaximumHeight) >
                Tolerance)
            {
                throw new InvalidOperationException(
                    "The church room and source bounds must stay aligned " +
                    "to the imported interior model height.");
            }

            ValidateZones(plan);
            ValidateFixtures(plan);
            ValidatePaths(plan);
            ValidateSanctuaryBoundary(plan);
        }

        private static void ValidateZones(
            ChurchInteriorLayoutPlan plan)
        {
            if (plan.Zones.Count != RequiredZoneCount)
            {
                throw new InvalidOperationException(
                    "The Catholic church must define narthex, nave, " +
                    "crossing/choir and sanctuary.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new HashSet<ChurchInteriorZoneKind>();
            for (int index = 0; index < plan.Zones.Count; index++)
            {
                ChurchInteriorZonePlan zone = plan.Zones[index];
                if (string.IsNullOrWhiteSpace(zone.Id) ||
                    !ids.Add(zone.Id) ||
                    !kinds.Add(zone.Kind) ||
                    !IsPositive(zone.Bounds.size) ||
                    !IsPositiveFinite(zone.CeilingHeight) ||
                    zone.CeilingHeight > plan.RoomHeight + Tolerance ||
                    !Contains(plan.RoomBounds, zone.Bounds))
                {
                    throw new InvalidOperationException(
                        "Every church zone must be unique, finite and " +
                        "inside the room.");
                }

                bool shouldBeAccessible =
                    zone.Kind != ChurchInteriorZoneKind.Sanctuary;
                if (zone.IsAccessible != shouldBeAccessible)
                {
                    throw new InvalidOperationException(
                        "Only the sanctuary may be inaccessible.");
                }
            }
        }

        private static void ValidateFixtures(
            ChurchInteriorLayoutPlan plan)
        {
            if (plan.Fixtures.Count != RequiredFixtureCount)
            {
                throw new InvalidOperationException(
                    "The Catholic furniture contract requires exactly " +
                    $"{RequiredFixtureCount} fixtures.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var counts = new Dictionary<ChurchInteriorFixtureKind, int>();
            for (int index = 0; index < plan.Fixtures.Count; index++)
            {
                ChurchInteriorFixturePlan fixture = plan.Fixtures[index];
                if (string.IsNullOrWhiteSpace(fixture.Id) ||
                    !ids.Add(fixture.Id) ||
                    !IsPositive(fixture.Bounds.size) ||
                    !IsFinite(fixture.BaseHeight) ||
                    fixture.BaseHeight < 0f ||
                    !IsPositiveFinite(fixture.Height) ||
                    fixture.BaseHeight + fixture.Height >
                    plan.RoomHeight + Tolerance ||
                    !Contains(plan.RoomBounds, fixture.Bounds))
                {
                    throw new InvalidOperationException(
                        "Every church fixture must be unique, finite and " +
                        "inside the room envelope.");
                }

                counts.TryGetValue(fixture.Kind, out int count);
                counts[fixture.Kind] = count + 1;
            }

            RequireCount(
                counts,
                ChurchInteriorFixtureKind.Pier,
                RequiredPierCount);
            RequireCount(
                counts,
                ChurchInteriorFixtureKind.AltarRail,
                1);
            RequireCount(
                counts,
                ChurchInteriorFixtureKind.AltarTable,
                1);
            RequireCount(
                counts,
                ChurchInteriorFixtureKind.HighAltar,
                1);
            RequireCount(
                counts,
                ChurchInteriorFixtureKind.Crucifix,
                1);
            RequireCount(
                counts,
                ChurchInteriorFixtureKind.Pew,
                RequiredPewCount);
            RequireCount(
                counts,
                ChurchInteriorFixtureKind.Confessional,
                RequiredConfessionalCount);
            RequireCount(
                counts,
                ChurchInteriorFixtureKind.VotiveCandleStand,
                RequiredVotiveStandCount);
            RequireCount(
                counts,
                ChurchInteriorFixtureKind.BaptismalFont,
                1);
            RequireCount(
                counts,
                ChurchInteriorFixtureKind.ChoirLoftSupport,
                RequiredChoirLoftSupportCount);
            RequireCount(
                counts,
                ChurchInteriorFixtureKind.ChoirLoft,
                1);
            RequireCount(
                counts,
                ChurchInteriorFixtureKind.Organ,
                1);
        }

        private static void ValidatePaths(
            ChurchInteriorLayoutPlan plan)
        {
            if (plan.Paths.Count != RequiredPathCount)
            {
                throw new InvalidOperationException(
                    "The church must define its main route, side aisles " +
                    "and two crossings.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new HashSet<ChurchInteriorPathKind>();
            for (int index = 0; index < plan.Paths.Count; index++)
            {
                ChurchInteriorPathPlan path = plan.Paths[index];
                if (string.IsNullOrWhiteSpace(path.Id) ||
                    !ids.Add(path.Id) ||
                    !kinds.Add(path.Kind) ||
                    !IsPositive(path.Bounds.size) ||
                    !IsPositiveFinite(path.MinimumClearance) ||
                    path.MinimumClearance < MinimumRouteClearance ||
                    !Contains(plan.WalkableBounds, path.Bounds))
                {
                    throw new InvalidOperationException(
                        "Every protected church route must be unique, " +
                        "wide enough and inside the walkable nave.");
                }

                for (int fixtureIndex = 0;
                     fixtureIndex < plan.Fixtures.Count;
                     fixtureIndex++)
                {
                    ChurchInteriorFixturePlan fixture =
                        plan.Fixtures[fixtureIndex];
                    if (BlocksGroundRoute(fixture) &&
                        path.Bounds.Overlaps(fixture.Bounds))
                    {
                        throw new InvalidOperationException(
                            $"Fixture '{fixture.Id}' blocks protected " +
                            $"route '{path.Id}'.");
                    }
                }
            }

            ChurchInteriorPathPlan main = RequirePath(
                plan,
                ChurchInteriorPathKind.MainNave);
            ChurchInteriorPathPlan north = RequirePath(
                plan,
                ChurchInteriorPathKind.NorthSideAisle);
            ChurchInteriorPathPlan south = RequirePath(
                plan,
                ChurchInteriorPathKind.SouthSideAisle);
            ChurchInteriorPathPlan narthex = RequirePath(
                plan,
                ChurchInteriorPathKind.NarthexCrossing);
            ChurchInteriorPathPlan transeptChoir = RequirePath(
                plan,
                ChurchInteriorPathKind.TranseptChoirCrossing);
            if (!Contains(main.Bounds, plan.PlayerSpawn))
            {
                throw new InvalidOperationException(
                    "The player spawn must open directly onto the " +
                    "protected main nave route.");
            }

            RequireConnected(main, narthex);
            RequireConnected(main, transeptChoir);
            RequireConnected(north, narthex);
            RequireConnected(north, transeptChoir);
            RequireConnected(south, narthex);
            RequireConnected(south, transeptChoir);
        }

        private static void ValidateSanctuaryBoundary(
            ChurchInteriorLayoutPlan plan)
        {
            if (!plan.TryGetZone(
                    ChurchInteriorZoneKind.Sanctuary,
                    out ChurchInteriorZonePlan sanctuary) ||
                sanctuary.IsAccessible)
            {
                throw new InvalidOperationException(
                    "The sanctuary must be present and inaccessible.");
            }

            ChurchInteriorFixturePlan altarRail = default;
            bool found = false;
            for (int index = 0; index < plan.Fixtures.Count; index++)
            {
                ChurchInteriorFixturePlan fixture = plan.Fixtures[index];
                if (fixture.Kind ==
                    ChurchInteriorFixtureKind.AltarRail)
                {
                    altarRail = fixture;
                    found = true;
                    break;
                }
            }

            if (!found ||
                !altarRail.BlocksMovement ||
                altarRail.Bounds.xMin >
                plan.WalkableBounds.xMin + Tolerance ||
                altarRail.Bounds.xMax <
                plan.WalkableBounds.xMax - Tolerance ||
                plan.WalkableBounds.yMax >
                altarRail.Bounds.yMin + Tolerance ||
                plan.WalkableBounds.yMax <
                altarRail.Bounds.yMin - 0.05f ||
                sanctuary.Bounds.yMin <
                altarRail.Bounds.yMax - Tolerance)
            {
                throw new InvalidOperationException(
                    "A continuous communion rail with a closed central " +
                    "gate and side returns must separate the nave from " +
                    "the sanctuary.");
            }
        }

        private static bool BlocksGroundRoute(
            ChurchInteriorFixturePlan fixture)
        {
            return fixture.BlocksMovement &&
                   fixture.BaseHeight < PlayerRouteClearHeight;
        }

        private static ChurchInteriorPathPlan RequirePath(
            ChurchInteriorLayoutPlan plan,
            ChurchInteriorPathKind kind)
        {
            if (!plan.TryGetPath(kind, out ChurchInteriorPathPlan path))
            {
                throw new InvalidOperationException(
                    $"The church is missing path '{kind}'.");
            }

            return path;
        }

        private static void RequireConnected(
            ChurchInteriorPathPlan first,
            ChurchInteriorPathPlan second)
        {
            Rect expanded = new Rect(
                first.Bounds.xMin - Tolerance,
                first.Bounds.yMin - Tolerance,
                first.Bounds.width + Tolerance * 2f,
                first.Bounds.height + Tolerance * 2f);
            if (!expanded.Overlaps(second.Bounds))
            {
                throw new InvalidOperationException(
                    $"Protected routes '{first.Id}' and '{second.Id}' " +
                    "must connect.");
            }
        }

        private static void RequireCount(
            IReadOnlyDictionary<ChurchInteriorFixtureKind, int> counts,
            ChurchInteriorFixtureKind kind,
            int required)
        {
            counts.TryGetValue(kind, out int actual);
            if (actual != required)
            {
                throw new InvalidOperationException(
                    $"The church requires {required} '{kind}' fixtures, " +
                    $"but received {actual}.");
            }
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - Tolerance &&
                   inner.xMax <= outer.xMax + Tolerance &&
                   inner.yMin >= outer.yMin - Tolerance &&
                   inner.yMax <= outer.yMax + Tolerance;
        }

        private static bool Contains(Rect bounds, Vector3 point)
        {
            return point.x >= bounds.xMin - Tolerance &&
                   point.x <= bounds.xMax + Tolerance &&
                   point.z >= bounds.yMin - Tolerance &&
                   point.z <= bounds.yMax + Tolerance &&
                   IsFinite(point);
        }

        private static bool IsPositive(Vector2 value)
        {
            return IsPositiveFinite(value.x) &&
                   IsPositiveFinite(value.y);
        }

        private static bool IsPositive(Vector3 value)
        {
            return IsPositiveFinite(value.x) &&
                   IsPositiveFinite(value.y) &&
                   IsPositiveFinite(value.z);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && IsFinite(value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
