using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class MothersHouseInteriorLayoutValidator
    {
        public const int RequiredPathCount = 3;
        public const int RequiredFixtureCount = 7;
        public const float MinimumRouteClearance = 1.2f;
        public const float MaximumExteriorWidth = 11f;
        public const float MaximumExteriorDepth = 9f;

        private const float Tolerance = 0.001f;
        private const float AnchorTolerance = 0.015f;
        private const float CameraAngleTolerance = 0.1f;
        private const float MinimumPathJunctionSpan = 0.64f;

        public static void ValidateOrThrow(
            MothersHouseInteriorLayoutPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            ValidateEnvelope(plan);
            ValidateAnchors(plan);
            ValidateFixtures(plan);
            ValidatePaths(plan);
            ValidateComposition(plan);
        }

        private static void ValidateEnvelope(
            MothersHouseInteriorLayoutPlan plan)
        {
            if (!IsPositive(plan.RoomSize) ||
                Mathf.Abs(
                    plan.RoomSize.x -
                    MothersHouseInteriorLayoutPlanner.RoomWidth) >
                    Tolerance ||
                Mathf.Abs(
                    plan.RoomSize.y -
                    MothersHouseInteriorLayoutPlanner.RoomDepth) >
                    Tolerance ||
                plan.RoomSize.x > MaximumExteriorWidth + Tolerance ||
                plan.RoomSize.y > MaximumExteriorDepth + Tolerance ||
                !IsPositiveFinite(plan.RoomHeight) ||
                !IsPositiveFinite(plan.WallThickness) ||
                Mathf.Abs(
                    plan.WallThickness -
                    MothersHouseInteriorLayoutPlanner.WallThickness) >
                    Tolerance ||
                !IsPositiveFinite(plan.DoorOpeningWidth) ||
                Mathf.Abs(
                    plan.DoorOpeningWidth -
                    MothersHouseInteriorLayoutPlanner.DoorOpeningWidth) >
                    Tolerance ||
                plan.DoorOpeningWidth >= plan.RoomSize.x ||
                !IsPositive(plan.RoomBounds.size) ||
                !RectMatch(
                    plan.RoomBounds,
                    MothersHouseInteriorLayoutPlanner.RoomBounds) ||
                !IsPositive(plan.WalkableBounds.size) ||
                !RectMatch(
                    plan.WalkableBounds,
                    MothersHouseInteriorLayoutPlanner.WalkableBounds) ||
                !Contains(plan.RoomBounds, plan.WalkableBounds) ||
                !IsFinite(plan.ModelLocalBounds.center) ||
                !IsPositive(plan.ModelLocalBounds.size) ||
                !BoundsMatch(
                    plan.ModelLocalBounds,
                    MothersHouseInteriorLayoutPlanner.ModelLocalBounds,
                    AnchorTolerance) ||
                !string.Equals(
                    plan.ModelResourcePath,
                    MothersHouseInteriorLayoutPlanner.ModelResourcePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The mother's house requires a finite 10 x 8 metre " +
                    "room, a contained walkable area and its typed model.");
            }

            Vector2 roomSize = plan.RoomBounds.size;
            if (Mathf.Abs(roomSize.x - plan.RoomSize.x) > Tolerance ||
                Mathf.Abs(roomSize.y - plan.RoomSize.y) > Tolerance ||
                Mathf.Abs(
                    plan.RoomHeight -
                    MothersHouseInteriorLayoutPlanner.RoomHeight) >
                Tolerance)
            {
                throw new InvalidOperationException(
                    "The room envelope must match the authored interior " +
                    "dimensions.");
            }
        }

        private static void ValidateAnchors(
            MothersHouseInteriorLayoutPlan plan)
        {
            if (!Contains(plan.WalkableBounds, plan.PlayerSpawn) ||
                !Contains(plan.WalkableBounds, plan.ExitPosition) ||
                !IsFinite(plan.EntryPosition) ||
                !IsPositive(plan.ExitTriggerSize) ||
                !Contains(plan.RoomBounds, plan.CameraTarget) ||
                !Contains(plan.RoomBounds, plan.WestWindowPosition) ||
                !Contains(plan.RoomBounds, plan.EastWindowPosition) ||
                !Contains(plan.RoomBounds, plan.FireplacePosition) ||
                !Contains(plan.RoomBounds, plan.FireLightPosition) ||
                !Contains(plan.RoomBounds, plan.FloorLampLightPosition) ||
                !Contains(plan.RoomBounds, plan.TabletopPosition) ||
                !Contains(plan.RoomBounds, plan.TeapotDockPosition))
            {
                throw new InvalidOperationException(
                    "Every gameplay and presentation anchor must be finite " +
                    "and placed against the room it belongs to.");
            }

            Vector3 expectedSpawn =
                MothersHouseInteriorLayoutPlanner.SpawnAnchorPosition +
                Vector3.up * PlayerFactory.GroundedRootOffset;
            Vector3 expectedExit =
                MothersHouseInteriorLayoutPlanner.ExitAnchorPosition +
                Vector3.up * 0.95f;
            if (Distance(
                    plan.EntryPosition,
                    MothersHouseInteriorLayoutPlanner
                        .EntryAnchorPosition) > AnchorTolerance ||
                Distance(plan.PlayerSpawn, expectedSpawn) >
                    AnchorTolerance ||
                Distance(plan.ExitPosition, expectedExit) >
                    AnchorTolerance ||
                Distance(
                    plan.WestWindowPosition,
                    MothersHouseInteriorLayoutPlanner
                        .WestWindowPosition) > AnchorTolerance ||
                Distance(
                    plan.EastWindowPosition,
                    MothersHouseInteriorLayoutPlanner
                        .EastWindowPosition) > AnchorTolerance ||
                Distance(
                    plan.FireplacePosition,
                    MothersHouseInteriorLayoutPlanner
                        .FireplaceAnchorPosition) > AnchorTolerance ||
                Distance(
                    plan.FireLightPosition,
                    MothersHouseInteriorLayoutPlanner
                        .FireLightAnchorPosition) > AnchorTolerance ||
                Distance(
                    plan.FloorLampLightPosition,
                    MothersHouseInteriorLayoutPlanner
                        .FloorLampLightAnchorPosition) > AnchorTolerance ||
                Distance(
                    plan.TabletopPosition,
                    MothersHouseInteriorLayoutPlanner
                        .TabletopAnchorPosition) > AnchorTolerance ||
                Distance(
                    plan.TeapotDockPosition,
                    MothersHouseInteriorLayoutPlanner
                        .TeapotDockAnchorPosition) > AnchorTolerance)
            {
                throw new InvalidOperationException(
                    "The pure layout anchors must match the imported " +
                    "mother's-house model contract.");
            }

            plan.CameraShot.Validate();
            if (plan.CameraShot.Kind != HomeCameraShotKind.MainRoom ||
                !plan.CameraShot.IsInActivationArea(plan.PlayerSpawn) ||
                Distance(
                    plan.CameraShot.Position,
                    MothersHouseInteriorLayoutPlanner.CameraPosition) >
                    AnchorTolerance ||
                Distance(
                    plan.CameraTarget,
                    MothersHouseInteriorLayoutPlanner.CameraTarget) >
                    AnchorTolerance ||
                Vector3.Angle(
                    plan.CameraShot.Rotation * Vector3.forward,
                    plan.CameraTarget - plan.CameraShot.Position) >
                    CameraAngleTolerance ||
                Mathf.Abs(
                    plan.CameraShot.FieldOfView -
                    MothersHouseInteriorLayoutPlanner
                        .CameraVerticalFieldOfView) > Tolerance ||
                !RectMatch(
                    plan.CameraShot.ActivationBounds,
                    plan.WalkableBounds) ||
                !RectMatch(
                    plan.CameraShot.HoldBounds,
                    plan.WalkableBounds) ||
                Mathf.Abs(plan.EntryPosition.z - plan.RoomBounds.yMin) >
                    0.2f ||
                Mathf.Abs(plan.EntryPosition.x - plan.ExitPosition.x) >
                    Tolerance ||
                Mathf.Abs(
                    plan.EntryPosition.x - plan.FireplacePosition.x) >
                    Tolerance)
            {
                throw new InvalidOperationException(
                    "The single fixed shot must preserve the approved " +
                    "wide southeast cutaway, south-wall entrance and " +
                    "two-window hearth composition.");
            }
        }

        private static void ValidateFixtures(
            MothersHouseInteriorLayoutPlan plan)
        {
            if (plan.Fixtures.Count != RequiredFixtureCount)
            {
                throw new InvalidOperationException(
                    "The mother's room requires a table, rocking chair, " +
                    "sofa, fireplace, cupboard, yarn basket and floor " +
                    "lamp.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new HashSet<MothersHouseInteriorFixtureKind>();
            for (int index = 0; index < plan.Fixtures.Count; index++)
            {
                MothersHouseInteriorFixturePlan fixture =
                    plan.Fixtures[index];
                if (string.IsNullOrWhiteSpace(fixture.Id) ||
                    !ids.Add(fixture.Id) ||
                    !kinds.Add(fixture.Kind) ||
                    !Enum.IsDefined(
                        typeof(MothersHouseInteriorFixtureKind),
                        fixture.Kind) ||
                    !IsPositive(fixture.Bounds.size) ||
                    !Contains(plan.RoomBounds, fixture.Bounds) ||
                    !IsFinite(fixture.BaseHeight) ||
                    fixture.BaseHeight < 0f ||
                    !IsPositiveFinite(fixture.Height) ||
                    fixture.BaseHeight + fixture.Height >
                        plan.RoomHeight + Tolerance)
                {
                    throw new InvalidOperationException(
                        "Every fixture must be unique, finite and contained " +
                        "by the room.");
                }

                for (int previous = 0; previous < index; previous++)
                {
                    if (fixture.Bounds.Overlaps(
                            plan.Fixtures[previous].Bounds))
                    {
                        throw new InvalidOperationException(
                            $"Fixtures '{fixture.Id}' and " +
                            $"'{plan.Fixtures[previous].Id}' overlap.");
                    }
                }
            }

            if (kinds.Count != RequiredFixtureCount)
            {
                throw new InvalidOperationException(
                    "Each required fixture kind must occur exactly once.");
            }
        }

        private static void ValidatePaths(
            MothersHouseInteriorLayoutPlan plan)
        {
            if (plan.Paths.Count != RequiredPathCount)
            {
                throw new InvalidOperationException(
                    "The room requires entry, main and table approach " +
                    "corridors.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new HashSet<MothersHouseInteriorPathKind>();
            for (int index = 0; index < plan.Paths.Count; index++)
            {
                MothersHouseInteriorPathPlan path = plan.Paths[index];
                if (string.IsNullOrWhiteSpace(path.Id) ||
                    !ids.Add(path.Id) ||
                    !kinds.Add(path.Kind) ||
                    !Enum.IsDefined(
                        typeof(MothersHouseInteriorPathKind),
                        path.Kind) ||
                    !IsPositive(path.Bounds.size) ||
                    !Contains(plan.WalkableBounds, path.Bounds) ||
                    !IsPositiveFinite(path.MinimumClearance) ||
                    path.MinimumClearance <
                        MinimumRouteClearance - Tolerance ||
                    Mathf.Min(
                        path.Bounds.width,
                        path.Bounds.height) <
                        path.MinimumClearance - Tolerance)
                {
                    throw new InvalidOperationException(
                        "Every protected route must be unique, contained " +
                        "and at least 1.2 metres wide.");
                }

                for (int fixtureIndex = 0;
                     fixtureIndex < plan.Fixtures.Count;
                     fixtureIndex++)
                {
                    MothersHouseInteriorFixturePlan fixture =
                        plan.Fixtures[fixtureIndex];
                    if (fixture.BlocksMovement &&
                        path.Bounds.Overlaps(fixture.Bounds))
                    {
                        throw new InvalidOperationException(
                            $"Fixture '{fixture.Id}' blocks protected " +
                            $"route '{path.Id}'.");
                    }
                }
            }

            MothersHouseInteriorPathPlan entry = RequirePath(
                plan,
                MothersHouseInteriorPathKind.EntryApproach);
            MothersHouseInteriorPathPlan main = RequirePath(
                plan,
                MothersHouseInteriorPathKind.MainPassage);
            MothersHouseInteriorPathPlan table = RequirePath(
                plan,
                MothersHouseInteriorPathKind.TableApproach);
            if (!Contains(entry.Bounds, plan.PlayerSpawn) ||
                !ConnectedWithClearance(entry.Bounds, main.Bounds) ||
                !ConnectedWithClearance(main.Bounds, table.Bounds))
            {
                throw new InvalidOperationException(
                    "The spawn must connect continuously to the central " +
                    "table without crossing furniture.");
            }
        }

        private static void ValidateComposition(
            MothersHouseInteriorLayoutPlan plan)
        {
            MothersHouseInteriorFixturePlan table = RequireFixture(
                plan,
                MothersHouseInteriorFixtureKind.LowTable);
            MothersHouseInteriorFixturePlan chair = RequireFixture(
                plan,
                MothersHouseInteriorFixtureKind.RockingChair);
            MothersHouseInteriorFixturePlan sofa = RequireFixture(
                plan,
                MothersHouseInteriorFixtureKind.Sofa);
            MothersHouseInteriorFixturePlan fireplace = RequireFixture(
                plan,
                MothersHouseInteriorFixtureKind.Fireplace);
            MothersHouseInteriorFixturePlan cupboard = RequireFixture(
                plan,
                MothersHouseInteriorFixtureKind.Cupboard);
            MothersHouseInteriorFixturePlan yarnBasket = RequireFixture(
                plan,
                MothersHouseInteriorFixtureKind.YarnBasket);
            MothersHouseInteriorFixturePlan floorLamp = RequireFixture(
                plan,
                MothersHouseInteriorFixtureKind.FloorLamp);

            if (table.Bounds.center.magnitude > 0.15f ||
                chair.Bounds.center.y <= table.Bounds.yMax ||
                sofa.Bounds.xMax >= table.Bounds.xMin ||
                Mathf.Abs(fireplace.Bounds.center.x) > Tolerance ||
                fireplace.Bounds.yMax <
                    plan.RoomBounds.yMax - Tolerance ||
                plan.WestWindowPosition.x >= fireplace.Bounds.xMin ||
                plan.EastWindowPosition.x <= fireplace.Bounds.xMax ||
                Mathf.Abs(
                    plan.WestWindowPosition.z -
                    plan.EastWindowPosition.z) > Tolerance ||
                !Contains(table.Bounds, plan.TabletopPosition) ||
                !Contains(table.Bounds, plan.TeapotDockPosition) ||
                Mathf.Abs(
                    plan.TabletopPosition.y - table.Height) > Tolerance ||
                plan.TeapotDockPosition.y < plan.TabletopPosition.y ||
                plan.TeapotDockPosition.y >
                    plan.TabletopPosition.y + 0.08f)
            {
                throw new InvalidOperationException(
                    "The room must keep the low table central, the chair " +
                    "north, the sofa west, and the fireplace between both " +
                    "north-wall windows.");
            }

            if (!cupboard.BlocksMovement ||
                !RectMatch(
                    cupboard.Bounds,
                    MothersHouseInteriorLayoutPlanner.CupboardBounds) ||
                Mathf.Abs(
                    cupboard.BaseHeight -
                    MothersHouseInteriorLayoutPlanner
                        .CupboardBaseHeight) > Tolerance ||
                Mathf.Abs(
                    cupboard.Height -
                    MothersHouseInteriorLayoutPlanner.CupboardHeight) >
                    Tolerance ||
                !yarnBasket.BlocksMovement ||
                !RectMatch(
                    yarnBasket.Bounds,
                    MothersHouseInteriorLayoutPlanner.YarnBasketBounds) ||
                Mathf.Abs(
                    yarnBasket.BaseHeight -
                    MothersHouseInteriorLayoutPlanner
                        .YarnBasketBaseHeight) > Tolerance ||
                Mathf.Abs(
                    yarnBasket.Height -
                    MothersHouseInteriorLayoutPlanner.YarnBasketHeight) >
                    Tolerance ||
                !floorLamp.BlocksMovement ||
                !RectMatch(
                    floorLamp.Bounds,
                    MothersHouseInteriorLayoutPlanner.FloorLampBounds) ||
                !Contains(floorLamp.Bounds, plan.FloorLampLightPosition) ||
                Mathf.Abs(
                    floorLamp.Height -
                    MothersHouseInteriorLayoutPlanner.FloorLampHeight) >
                    Tolerance)
            {
                throw new InvalidOperationException(
                    "The cupboard, yarn basket and floor lamp must keep " +
                    "their authored blocking proxies.");
            }

            if (DistanceXZ(
                    plan.FireplacePosition,
                    fireplace.Center) > 1.3f ||
                plan.FireLightPosition.z >=
                    plan.RoomBounds.yMax - plan.WallThickness ||
                DistanceXZ(
                    plan.FloorLampLightPosition,
                    sofa.Center) > 2f ||
                plan.FloorLampLightPosition.y <= sofa.Height ||
                plan.FloorLampLightPosition.y >= plan.RoomHeight)
            {
                throw new InvalidOperationException(
                    "The visible fire and floor-lamp light must stay on " +
                    "their authored hearth and sofa-side fixtures.");
            }
        }

        private static MothersHouseInteriorPathPlan RequirePath(
            MothersHouseInteriorLayoutPlan plan,
            MothersHouseInteriorPathKind kind)
        {
            if (!plan.TryGetPath(kind, out MothersHouseInteriorPathPlan path))
            {
                throw new InvalidOperationException(
                    $"The layout is missing path '{kind}'.");
            }

            return path;
        }

        private static MothersHouseInteriorFixturePlan RequireFixture(
            MothersHouseInteriorLayoutPlan plan,
            MothersHouseInteriorFixtureKind kind)
        {
            if (!plan.TryGetFixture(
                    kind,
                    out MothersHouseInteriorFixturePlan fixture))
            {
                throw new InvalidOperationException(
                    $"The layout is missing fixture '{kind}'.");
            }

            return fixture;
        }

        private static bool ConnectedWithClearance(
            Rect first,
            Rect second)
        {
            float overlapX = Mathf.Min(first.xMax, second.xMax) -
                             Mathf.Max(first.xMin, second.xMin);
            float overlapY = Mathf.Min(first.yMax, second.yMax) -
                             Mathf.Max(first.yMin, second.yMin);
            return overlapX >= MinimumPathJunctionSpan - Tolerance &&
                   overlapY >= MinimumPathJunctionSpan - Tolerance;
        }

        private static bool BoundsMatch(
            Bounds actual,
            Bounds expected,
            float tolerance)
        {
            return Distance(actual.min, expected.min) <= tolerance &&
                   Distance(actual.max, expected.max) <= tolerance;
        }

        private static bool RectMatch(Rect actual, Rect expected)
        {
            return Mathf.Abs(actual.xMin - expected.xMin) <= Tolerance &&
                   Mathf.Abs(actual.xMax - expected.xMax) <= Tolerance &&
                   Mathf.Abs(actual.yMin - expected.yMin) <= Tolerance &&
                   Mathf.Abs(actual.yMax - expected.yMax) <= Tolerance;
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
            return IsFinite(point) &&
                   point.x >= bounds.xMin - Tolerance &&
                   point.x <= bounds.xMax + Tolerance &&
                   point.z >= bounds.yMin - Tolerance &&
                   point.z <= bounds.yMax + Tolerance;
        }

        private static float Distance(Vector3 first, Vector3 second)
        {
            return (first - second).magnitude;
        }

        private static float DistanceXZ(Vector3 first, Vector3 second)
        {
            return new Vector2(
                first.x - second.x,
                first.z - second.z).magnitude;
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
