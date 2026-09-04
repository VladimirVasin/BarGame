using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class MothersHouseInteriorLayoutValidator
    {
        public const int RequiredPathCount = 6;
        public const int RequiredFixtureCount = 22;
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
            ValidateUpperFloor(plan);
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
                plan.UpperFloor == null ||
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

            ValidateCameraShots(plan);
            if (Distance(
                    plan.CameraTarget,
                    MothersHouseInteriorLayoutPlanner.CameraTarget) >
                    AnchorTolerance ||
                Mathf.Abs(plan.EntryPosition.z - plan.RoomBounds.yMin) >
                    0.2f ||
                Mathf.Abs(plan.EntryPosition.x - plan.ExitPosition.x) >
                    Tolerance ||
                Mathf.Abs(
                    plan.EntryPosition.x - plan.FireplacePosition.x) >
                    Tolerance)
            {
                throw new InvalidOperationException(
                    "The ground floor must preserve the approved south-wall " +
                    "entrance and two-window hearth composition.");
            }
        }

        private static void ValidateCameraShots(
            MothersHouseInteriorLayoutPlan plan)
        {
            if (plan.CameraShots.Count != 4)
            {
                throw new InvalidOperationException(
                    "The two-storey mother's house requires four fixed " +
                    "gameplay shots.");
            }

            var kinds = new HashSet<HomeCameraShotKind>();
            for (int index = 0; index < plan.CameraShots.Count; index++)
            {
                HomeCameraShot candidate = plan.CameraShots[index];
                candidate.Validate();
                if (!kinds.Add(candidate.Kind))
                {
                    throw new InvalidOperationException(
                        $"Camera shot '{candidate.Kind}' is duplicated.");
                }
            }

            ValidateCameraShot(
                RequireCameraShot(plan, HomeCameraShotKind.MainRoom),
                MothersHouseInteriorLayoutPlanner.CameraPosition,
                MothersHouseInteriorLayoutPlanner.CameraTarget,
                MothersHouseInteriorLayoutPlanner.CameraVerticalFieldOfView);
            ValidateCameraShot(
                RequireCameraShot(
                    plan,
                    HomeCameraShotKind.StairAndUpperCorridor),
                MothersHouseInteriorLayoutPlanner.StairCameraPosition,
                MothersHouseInteriorLayoutPlanner.StairCameraTarget,
                MothersHouseInteriorLayoutPlanner
                    .UpperCameraVerticalFieldOfView);
            ValidateCameraShot(
                RequireCameraShot(
                    plan,
                    HomeCameraShotKind.UpperSouthRoom),
                MothersHouseInteriorLayoutPlanner.SouthRoomCameraPosition,
                MothersHouseInteriorLayoutPlanner.SouthRoomCameraTarget,
                MothersHouseInteriorLayoutPlanner
                    .UpperCameraVerticalFieldOfView);
            ValidateCameraShot(
                RequireCameraShot(
                    plan,
                    HomeCameraShotKind.UpperNorthRoom),
                MothersHouseInteriorLayoutPlanner.NorthRoomCameraPosition,
                MothersHouseInteriorLayoutPlanner.NorthRoomCameraTarget,
                MothersHouseInteriorLayoutPlanner
                    .UpperCameraVerticalFieldOfView);

            HomeCameraShot ground = plan.CameraShot;
            if (ground.Kind != HomeCameraShotKind.MainRoom ||
                !ground.IsInActivationArea(plan.PlayerSpawn) ||
                !RectMatch(ground.ActivationBounds, plan.WalkableBounds) ||
                !RectMatch(ground.HoldBounds, plan.WalkableBounds))
            {
                throw new InvalidOperationException(
                    "The approved wide southeast ground-floor shot drifted.");
            }
        }

        private static void ValidateCameraShot(
            HomeCameraShot shot,
            Vector3 expectedPosition,
            Vector3 expectedTarget,
            float expectedFieldOfView)
        {
            if (Distance(shot.Position, expectedPosition) > AnchorTolerance ||
                Vector3.Angle(
                    shot.Rotation * Vector3.forward,
                    expectedTarget - shot.Position) >
                    CameraAngleTolerance ||
                Mathf.Abs(shot.FieldOfView - expectedFieldOfView) >
                    Tolerance)
            {
                throw new InvalidOperationException(
                    $"Camera shot '{shot.Kind}' drifted from its pure pose.");
            }
        }

        private static HomeCameraShot RequireCameraShot(
            MothersHouseInteriorLayoutPlan plan,
            HomeCameraShotKind kind)
        {
            if (!plan.TryGetCameraShot(kind, out HomeCameraShot shot))
            {
                throw new InvalidOperationException(
                    $"The layout is missing camera shot '{kind}'.");
            }

            return shot;
        }

        private static void ValidateUpperFloor(
            MothersHouseInteriorLayoutPlan plan)
        {
            MothersHouseInteriorUpperFloorPlan upper = plan.UpperFloor;
            StairwellFlightPlan stair = upper.StairFlight;
            float pitch = Mathf.Atan2(
                stair.TopElevation - stair.BaseElevation,
                stair.RunLength) * Mathf.Rad2Deg;
            if (Mathf.Abs(
                    upper.FloorElevation -
                    MothersHouseInteriorLayoutPlanner.UpperFloorElevation) >
                    Tolerance ||
                Mathf.Abs(
                    upper.CeilingHeight -
                    MothersHouseInteriorLayoutPlanner.UpperCeilingHeight) >
                    Tolerance ||
                upper.FloorElevation <= plan.RoomHeight ||
                upper.CeilingHeight - upper.FloorElevation < 2.2f ||
                !RectMatch(
                    upper.StairOpeningBounds,
                    MothersHouseInteriorLayoutPlanner.StairOpeningBounds) ||
                !RectMatch(
                    upper.CorridorBounds,
                    MothersHouseInteriorLayoutPlanner.UpperCorridorBounds) ||
                !RectMatch(
                    upper.SouthRoomBounds,
                    MothersHouseInteriorLayoutPlanner
                        .UpperSouthRoomBounds) ||
                !RectMatch(
                    upper.NorthRoomBounds,
                    MothersHouseInteriorLayoutPlanner
                        .UpperNorthRoomBounds) ||
                upper.SouthRoomBounds.Overlaps(upper.NorthRoomBounds) ||
                Mathf.Abs(
                    upper.PartitionX -
                    MothersHouseInteriorLayoutPlanner.UpperPartitionX) >
                    Tolerance ||
                Mathf.Abs(
                    upper.PartitionThickness -
                    MothersHouseInteriorLayoutPlanner
                        .UpperPartitionThickness) > Tolerance ||
                Mathf.Abs(
                    upper.RoomDividerZ -
                    MothersHouseInteriorLayoutPlanner.UpperRoomDividerZ) >
                    Tolerance ||
                upper.DoorOpeningWidth < MinimumRouteClearance - Tolerance ||
                Mathf.Abs(
                    upper.DoorOpeningHeight -
                    MothersHouseInteriorLayoutPlanner
                        .UpperDoorOpeningHeight) > Tolerance ||
                Mathf.Abs(
                    upper.SouthDoorCenterZ -
                    MothersHouseInteriorLayoutPlanner
                        .UpperSouthDoorCenterZ) > Tolerance ||
                Mathf.Abs(
                    upper.NorthDoorCenterZ -
                    MothersHouseInteriorLayoutPlanner
                        .UpperNorthDoorCenterZ) > Tolerance ||
                string.IsNullOrWhiteSpace(stair.Id) ||
                stair.StepCount !=
                    MothersHouseInteriorLayoutPlanner.StairStepCount ||
                Mathf.Abs(
                    stair.StepRise -
                    MothersHouseInteriorLayoutPlanner.StairStepRise) >
                    Tolerance ||
                Mathf.Abs(
                    stair.StepDepth -
                    MothersHouseInteriorLayoutPlanner.StairStepDepth) >
                    Tolerance ||
                Mathf.Abs(
                    stair.Width -
                    MothersHouseInteriorLayoutPlanner.StairWidth) >
                    Tolerance ||
                Mathf.Abs(stair.BaseElevation) > Tolerance ||
                Mathf.Abs(stair.TopElevation - upper.FloorElevation) >
                    Tolerance ||
                Vector2.Distance(stair.Start, new Vector2(-4f, 1.80f)) >
                    Tolerance ||
                Vector2.Distance(stair.Direction, Vector2.down) > Tolerance ||
                pitch >= 45f)
            {
                throw new InvalidOperationException(
                    "The upper storey must keep one continuous safe stair, " +
                    "a real corridor and exactly two furnished rooms.");
            }

            float halfDoor = upper.DoorOpeningWidth * 0.5f;
            if (upper.SouthDoorCenterZ - halfDoor <=
                    upper.SouthRoomBounds.yMin ||
                upper.SouthDoorCenterZ + halfDoor >=
                    upper.SouthRoomBounds.yMax ||
                upper.NorthDoorCenterZ - halfDoor <=
                    upper.NorthRoomBounds.yMin ||
                upper.NorthDoorCenterZ + halfDoor >=
                    upper.NorthRoomBounds.yMax ||
                upper.CorridorBounds.width <
                    MinimumRouteClearance - Tolerance)
            {
                throw new InvalidOperationException(
                    "Both upper rooms must open from a capsule-clear corridor.");
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
                    "lamp, and the storey above it the flue, both beds, " +
                    "the chest, the bedside table, the chair and the " +
                    "corridor's linen chest.");
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
                        CeilingAbove(plan, fixture.BaseHeight) + Tolerance)
                {
                    throw new InvalidOperationException(
                        "Every fixture must be unique, finite and contained " +
                        "by the room.");
                }

                for (int previous = 0; previous < index; previous++)
                {
                    MothersHouseInteriorFixturePlan other =
                        plan.Fixtures[previous];

                    // The two storeys share the same X/Z footprint, so a
                    // bed upstairs sits directly over the sofa downstairs.
                    // Only fixtures whose heights actually meet can clash.
                    if (SharesHeight(fixture, other) &&
                        fixture.Bounds.Overlaps(other.Bounds))
                    {
                        throw new InvalidOperationException(
                            $"Fixtures '{fixture.Id}' and " +
                            $"'{other.Id}' overlap.");
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

                    // A route is only obstructed by what stands on its own
                    // floor: the upper rooms sit directly above the lower
                    // one and share every X/Z coordinate with it.
                    if (fixture.BlocksMovement &&
                        StandsOnFloor(plan, fixture, path.FloorElevation) &&
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

            ValidateUpperRoutes(plan);
        }

        /// <summary>
        /// Upstairs the corridor and the two rooms are separated by a real
        /// partition, so their routes cannot touch the way ground-floor
        /// lanes do. They join through the doorways instead, and each side
        /// has to reach its own face of that partition.
        /// </summary>
        private static void ValidateUpperRoutes(
            MothersHouseInteriorLayoutPlan plan)
        {
            MothersHouseInteriorUpperFloorPlan upper = plan.UpperFloor;
            MothersHouseInteriorPathPlan corridor = RequirePath(
                plan,
                MothersHouseInteriorPathKind.UpperCorridorRun);
            MothersHouseInteriorPathPlan north = RequirePath(
                plan,
                MothersHouseInteriorPathKind.UpperNorthApproach);
            MothersHouseInteriorPathPlan south = RequirePath(
                plan,
                MothersHouseInteriorPathKind.UpperSouthApproach);

            float corridorFace =
                upper.PartitionX - upper.PartitionThickness * 0.5f;
            float roomFace =
                upper.PartitionX + upper.PartitionThickness * 0.5f;
            if (corridor.FloorElevation < upper.FloorElevation - Tolerance ||
                north.FloorElevation < upper.FloorElevation - Tolerance ||
                south.FloorElevation < upper.FloorElevation - Tolerance ||
                Mathf.Abs(corridor.Bounds.xMax - corridorFace) > Tolerance ||
                Mathf.Abs(north.Bounds.xMin - roomFace) > Tolerance ||
                Mathf.Abs(south.Bounds.xMin - roomFace) > Tolerance ||
                !Contains(upper.CorridorBounds, corridor.Bounds) ||
                !Contains(upper.NorthRoomBounds, north.Bounds) ||
                !Contains(upper.SouthRoomBounds, south.Bounds))
            {
                throw new InvalidOperationException(
                    "Both upper routes must stand on the upper floor and " +
                    "meet the partition they pass through.");
            }

            float halfDoor = upper.DoorOpeningWidth * 0.5f;
            if (!SpansDoorway(
                    corridor.Bounds,
                    upper.NorthDoorCenterZ,
                    halfDoor) ||
                !SpansDoorway(
                    corridor.Bounds,
                    upper.SouthDoorCenterZ,
                    halfDoor) ||
                !SpansDoorway(
                    north.Bounds,
                    upper.NorthDoorCenterZ,
                    halfDoor) ||
                !SpansDoorway(
                    south.Bounds,
                    upper.SouthDoorCenterZ,
                    halfDoor))
            {
                throw new InvalidOperationException(
                    "Every upper route must cover the full clear width of " +
                    "the doorway it serves.");
            }
        }

        private static bool SpansDoorway(
            Rect route,
            float doorCenterZ,
            float halfDoor)
        {
            return route.yMin <= doorCenterZ - halfDoor + Tolerance &&
                   route.yMax >= doorCenterZ + halfDoor - Tolerance;
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

            ValidateUpperComposition(plan);
        }

        /// <summary>
        /// The furnished storey above: the flue carries the hearth up the
        /// north wall, the double bed stands against it, the childhood bed
        /// stands in the other room, and each room keeps its own window.
        /// </summary>
        private static void ValidateUpperComposition(
            MothersHouseInteriorLayoutPlan plan)
        {
            MothersHouseInteriorUpperFloorPlan upper = plan.UpperFloor;
            MothersHouseInteriorFixturePlan flue = RequireFixture(
                plan,
                MothersHouseInteriorFixtureKind.UpperChimney);
            MothersHouseInteriorFixturePlan doubleBed = RequireFixture(
                plan,
                MothersHouseInteriorFixtureKind.UpperNorthBed);
            MothersHouseInteriorFixturePlan childBed = RequireFixture(
                plan,
                MothersHouseInteriorFixtureKind.UpperSouthBed);
            MothersHouseInteriorFixturePlan chest = RequireFixture(
                plan,
                MothersHouseInteriorFixtureKind.UpperNorthChest);
            MothersHouseInteriorFixturePlan bedside = RequireFixture(
                plan,
                MothersHouseInteriorFixtureKind.UpperNorthBedside);
            MothersHouseInteriorFixturePlan chair = RequireFixture(
                plan,
                MothersHouseInteriorFixtureKind.UpperSouthChair);
            MothersHouseInteriorFixturePlan linenChest = RequireFixture(
                plan,
                MothersHouseInteriorFixtureKind.UpperCorridorChest);

            // The flue is the ground-floor hearth continued, so it keeps the
            // hearth's own centre line and reaches the ceiling.
            if (Mathf.Abs(flue.Bounds.center.x -
                    plan.FireplacePosition.x) > 0.08f ||
                flue.Bounds.yMax < upper.NorthRoomBounds.yMax - Tolerance ||
                Mathf.Abs(flue.BaseHeight - upper.FloorElevation) >
                    Tolerance ||
                Mathf.Abs(
                    flue.BaseHeight + flue.Height -
                    upper.CeilingHeight) > Tolerance)
            {
                throw new InvalidOperationException(
                    "The upper flue must continue the hearth to the ceiling.");
            }

            if (!Contains(upper.NorthRoomBounds, doubleBed.Bounds) ||
                !Contains(upper.NorthRoomBounds, chest.Bounds) ||
                !Contains(upper.NorthRoomBounds, bedside.Bounds) ||
                !Contains(upper.SouthRoomBounds, childBed.Bounds) ||
                !Contains(upper.SouthRoomBounds, chair.Bounds) ||
                !Contains(upper.CorridorBounds, linenChest.Bounds))
            {
                throw new InvalidOperationException(
                    "Every upper fixture must stand in the room it belongs " +
                    "to.");
            }

            // The double bed is the wide one and stands head to the warm
            // wall; the childhood bed is narrower and shorter than it.
            if (doubleBed.Bounds.width <= childBed.Bounds.width ||
                doubleBed.Bounds.height <= childBed.Bounds.height ||
                doubleBed.Bounds.width < 1.3f ||
                doubleBed.Bounds.yMax <
                    upper.NorthRoomBounds.yMax - 0.12f ||
                childBed.Bounds.yMin >
                    upper.SouthRoomBounds.yMin + 0.12f ||
                bedside.Bounds.yMax < doubleBed.Bounds.yMax - 0.05f)
            {
                throw new InvalidOperationException(
                    "The parents' bed must be the wide one against the warm " +
                    "wall, with the bedside table at its head.");
            }

            // Each bedroom gets its own window, in its own wall.
            if (plan.UpperFloor.NorthWindowPosition.z <=
                    upper.NorthRoomBounds.yMax - 0.4f ||
                plan.UpperFloor.SouthWindowPosition.z >=
                    upper.SouthRoomBounds.yMin + 0.4f ||
                plan.UpperFloor.NorthWindowPosition.x >= flue.Bounds.xMin ||
                plan.UpperFloor.NorthWindowPosition.x <=
                    upper.NorthRoomBounds.xMin ||
                plan.UpperFloor.SouthWindowPosition.x <=
                    upper.SouthRoomBounds.xMin ||
                plan.UpperFloor.NorthWindowPosition.y <= upper.FloorElevation ||
                plan.UpperFloor.SouthWindowPosition.y <= upper.FloorElevation ||
                plan.UpperFloor.NorthWindowPosition.y >= upper.CeilingHeight ||
                plan.UpperFloor.SouthWindowPosition.y >= upper.CeilingHeight)
            {
                throw new InvalidOperationException(
                    "Both bedrooms must keep one window in their own outer " +
                    "wall, clear of the flue.");
            }

            // Each bedroom owns one hanging light. Both must clear a standing
            // head and hang over open floor rather than over a bed.
            if (!HangsOverOpenFloor(
                    plan.UpperFloor.NorthLampPosition,
                    upper,
                    upper.NorthRoomBounds,
                    doubleBed.Bounds) ||
                !HangsOverOpenFloor(
                    plan.UpperFloor.SouthLampPosition,
                    upper,
                    upper.SouthRoomBounds,
                    childBed.Bounds))
            {
                throw new InvalidOperationException(
                    "Each bedroom lamp must hang over open floor in its own " +
                    "room, above a standing hero.");
            }
        }

        private static bool HangsOverOpenFloor(
            Vector3 lamp,
            MothersHouseInteriorUpperFloorPlan upper,
            Rect room,
            Rect bed)
        {
            var footprint = new Vector2(lamp.x, lamp.z);
            return room.Contains(footprint) &&
                   !bed.Contains(footprint) &&
                   lamp.y < upper.CeilingHeight &&
                   lamp.y > upper.FloorElevation + 1.8f;
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

        /// <summary>
        /// The ceiling that closes over a fixture standing at this height.
        /// The house is two storeys, so "the ceiling" is not one number.
        /// </summary>
        private static float CeilingAbove(
            MothersHouseInteriorLayoutPlan plan,
            float baseHeight)
        {
            return baseHeight >= plan.UpperFloor.FloorElevation - Tolerance
                ? plan.UpperFloor.CeilingHeight
                : plan.RoomHeight;
        }

        /// <summary>
        /// Whether two fixtures occupy any of the same height at all. Two
        /// that do not can share a footprint: one is simply above the other.
        /// </summary>
        private static bool SharesHeight(
            MothersHouseInteriorFixturePlan first,
            MothersHouseInteriorFixturePlan second)
        {
            return first.BaseHeight <
                       second.BaseHeight + second.Height - Tolerance &&
                   second.BaseHeight <
                       first.BaseHeight + first.Height - Tolerance;
        }

        private static bool StandsOnFloor(
            MothersHouseInteriorLayoutPlan plan,
            MothersHouseInteriorFixturePlan fixture,
            float floorElevation)
        {
            bool fixtureIsUpstairs =
                fixture.BaseHeight >=
                    plan.UpperFloor.FloorElevation - Tolerance;
            bool routeIsUpstairs =
                floorElevation >=
                    plan.UpperFloor.FloorElevation - Tolerance;
            return fixtureIsUpstairs == routeIsUpstairs;
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
