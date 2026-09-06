using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class HomeInteriorLayoutValidator
    {
        public const float PlayerClearanceRadius = 0.35f;
        public const float MinimumPathClearance = 1f;
        public const float MinimumBathroomDoorwayClearance = 1f;

        private const float Tolerance = 0.001f;

        private static readonly HomeInteriorZoneKind[] RequiredZoneKinds =
        {
            HomeInteriorZoneKind.MainRoom,
            HomeInteriorZoneKind.Bathroom
        };

        private static readonly HomeInteriorPathKind[] RequiredPathKinds =
        {
            HomeInteriorPathKind.Entry,
            HomeInteriorPathKind.Main,
            HomeInteriorPathKind.BathroomAccess
        };

        private static readonly HomeFurnitureKind[] RequiredFurnitureKinds =
        {
            HomeFurnitureKind.Bed,
            HomeFurnitureKind.Kitchen,
            HomeFurnitureKind.Sofa,
            HomeFurnitureKind.Table,
            HomeFurnitureKind.Bookcase,
            HomeFurnitureKind.CameraCornerJunk,
            HomeFurnitureKind.Toilet,
            HomeFurnitureKind.Shower,
            HomeFurnitureKind.Sink
        };

        public static void ValidateOrThrow(
            HomeInteriorLayoutPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            ValidateRoom(plan);
            ValidateZones(plan);
            ValidatePaths(plan);
            ValidateFurniture(plan);
            HomeLockedRoomPlan.ValidateOrThrow(plan);
        }

        private static void ValidateRoom(
            HomeInteriorLayoutPlan plan)
        {
            if (!IsPositiveFinite(plan.RoomSize.x) ||
                !IsPositiveFinite(plan.RoomSize.y) ||
                !IsPositiveFinite(plan.RoomHeight))
            {
                throw new InvalidOperationException(
                    "The home room dimensions must be positive and finite.");
            }

            ValidateRect(
                plan.WalkableBounds,
                "The home walkable bounds");
            if (!Contains(plan.RoomBounds, plan.WalkableBounds))
            {
                throw new InvalidOperationException(
                    "The home walkable bounds must stay inside the room.");
            }

            if (!IsFinite(plan.PlayerSpawn) ||
                !IsFinite(plan.ExitPosition) ||
                !IsPositiveFinite(plan.ExitTriggerSize.x) ||
                !IsPositiveFinite(plan.ExitTriggerSize.y) ||
                !IsPositiveFinite(plan.ExitTriggerSize.z))
            {
                throw new InvalidOperationException(
                    "The home spawn, exit and exit trigger must be finite, " +
                    "and the trigger dimensions must be positive.");
            }

            if (!Contains(
                    plan.WalkableBounds,
                    plan.PlayerSpawn,
                    PlayerClearanceRadius) ||
                !Contains(
                    plan.WalkableBounds,
                    plan.ExitPosition,
                    0f))
            {
                throw new InvalidOperationException(
                    "The home spawn and exit must lie inside " +
                    "the walkable room.");
            }

            ValidateRect(
                plan.EntryCorridor,
                "The home entry corridor");
            if (!Contains(
                    plan.WalkableBounds,
                    plan.EntryCorridor))
            {
                throw new InvalidOperationException(
                    "The home entry corridor must stay inside " +
                    "the walkable room.");
            }

            ValidateRect(
                plan.BathroomBounds,
                "The bathroom bounds");
            if (!Contains(
                    plan.WalkableBounds,
                    plan.BathroomBounds))
            {
                throw new InvalidOperationException(
                    "The bathroom must stay inside the walkable room.");
            }

            ValidateRect(
                plan.BathroomDoorway,
                "The bathroom doorway");
            if (!Contains(
                    plan.WalkableBounds,
                    plan.BathroomDoorway) ||
                plan.BathroomDoorway.width + Tolerance <
                    MinimumBathroomDoorwayClearance ||
                !plan.BathroomDoorway.Overlaps(
                    plan.BathroomBounds,
                    true) ||
                plan.BathroomDoorway.yMin >=
                    plan.BathroomBounds.yMin ||
                plan.BathroomDoorway.yMax <=
                    plan.BathroomBounds.yMin)
            {
                throw new InvalidOperationException(
                    "The bathroom doorway must provide a controller-safe " +
                    "opening across the bathroom's near wall.");
            }
        }

        private static void ValidateZones(
            HomeInteriorLayoutPlan plan)
        {
            if (plan.Zones.Count != RequiredZoneKinds.Length)
            {
                throw new InvalidOperationException(
                    "The home layout requires exactly one main room " +
                    "and one bathroom zone.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new HashSet<HomeInteriorZoneKind>();
            for (int index = 0; index < plan.Zones.Count; index++)
            {
                HomeInteriorZone zone = plan.Zones[index];
                if (string.IsNullOrWhiteSpace(zone.Id) ||
                    !ids.Add(zone.Id))
                {
                    throw new InvalidOperationException(
                        "Home zone IDs must be non-empty and unique.");
                }

                if (!Enum.IsDefined(
                        typeof(HomeInteriorZoneKind),
                        zone.Kind) ||
                    !kinds.Add(zone.Kind))
                {
                    throw new InvalidOperationException(
                        "Home zone kinds must be valid and unique.");
                }

                ValidateRect(
                    zone.Bounds,
                    $"Home zone '{zone.Id}'");
                if (!Contains(
                        plan.WalkableBounds,
                        zone.Bounds))
                {
                    throw new InvalidOperationException(
                        $"Home zone '{zone.Id}' must stay inside " +
                        "the walkable room.");
                }
            }

            EnsureRequiredKinds(
                kinds,
                RequiredZoneKinds,
                "home zone");
            plan.TryGetZone(
                HomeInteriorZoneKind.MainRoom,
                out HomeInteriorZone mainRoom);
            plan.TryGetZone(
                HomeInteriorZoneKind.Bathroom,
                out HomeInteriorZone bathroom);
            if (!Approximately(
                    mainRoom.Bounds,
                    plan.WalkableBounds) ||
                !Approximately(
                    bathroom.Bounds,
                    plan.BathroomBounds))
            {
                throw new InvalidOperationException(
                    "Home zones must match the main walkable room " +
                    "and the declared bathroom bounds.");
            }
        }

        private static void ValidatePaths(
            HomeInteriorLayoutPlan plan)
        {
            if (plan.Paths.Count != RequiredPathKinds.Length)
            {
                throw new InvalidOperationException(
                    "The home layout requires entry, main and " +
                    "bathroom-access paths.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new HashSet<HomeInteriorPathKind>();
            for (int index = 0; index < plan.Paths.Count; index++)
            {
                HomeInteriorPath path = plan.Paths[index];
                if (string.IsNullOrWhiteSpace(path.Id) ||
                    !ids.Add(path.Id))
                {
                    throw new InvalidOperationException(
                        "Home path IDs must be non-empty and unique.");
                }

                if (!Enum.IsDefined(
                        typeof(HomeInteriorPathKind),
                        path.Kind) ||
                    !kinds.Add(path.Kind))
                {
                    throw new InvalidOperationException(
                        "Home path kinds must be valid and unique.");
                }

                ValidateRect(
                    path.Bounds,
                    $"Home path '{path.Id}'");
                if (!Contains(
                        plan.WalkableBounds,
                        path.Bounds) ||
                    !IsPositiveFinite(path.Clearance) ||
                    path.Clearance + Tolerance <
                        MinimumPathClearance ||
                    path.Bounds.width + Tolerance <
                        path.Clearance ||
                    path.Bounds.height + Tolerance <
                        path.Clearance)
                {
                    throw new InvalidOperationException(
                        $"Home path '{path.Id}' must fit the room and " +
                        "provide controller-safe clearance.");
                }
            }

            EnsureRequiredKinds(
                kinds,
                RequiredPathKinds,
                "home path");
            EnsurePathsConnected(plan.Paths);

            plan.TryGetPath(
                HomeInteriorPathKind.Entry,
                out HomeInteriorPath entry);
            plan.TryGetPath(
                HomeInteriorPathKind.Main,
                out HomeInteriorPath main);
            plan.TryGetPath(
                HomeInteriorPathKind.BathroomAccess,
                out HomeInteriorPath bathroomAccess);
            if (!Contains(
                    entry.Bounds,
                    plan.EntryCorridor) ||
                !Contains(
                    main.Bounds,
                    plan.PlayerSpawn,
                    0f) ||
                !Contains(
                    main.Bounds,
                    plan.ExitPosition,
                    0f) ||
                !Contains(
                    main.Bounds,
                    plan.BathroomDoorway) ||
                !Contains(
                    bathroomAccess.Bounds,
                    plan.BathroomDoorway) ||
                !bathroomAccess.Bounds.Overlaps(
                    plan.BathroomBounds,
                    true))
            {
                throw new InvalidOperationException(
                    "Reserved home paths must cover the entry, spawn, " +
                    "exit and bathroom doorway.");
            }
        }

        private static void ValidateFurniture(
            HomeInteriorLayoutPlan plan)
        {
            if (plan.Furniture.Count != RequiredFurnitureKinds.Length)
            {
                throw new InvalidOperationException(
                    "The home layout requires exactly six living " +
                    "furniture groups and three bathroom fixtures.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new HashSet<HomeFurnitureKind>();
            for (int index = 0;
                 index < plan.Furniture.Count;
                 index++)
            {
                HomeFurnitureFootprint footprint =
                    plan.Furniture[index];
                if (string.IsNullOrWhiteSpace(footprint.Id) ||
                    !ids.Add(footprint.Id))
                {
                    throw new InvalidOperationException(
                        "Home furniture IDs must be non-empty and unique.");
                }

                if (!Enum.IsDefined(
                        typeof(HomeFurnitureKind),
                        footprint.Kind) ||
                    !kinds.Add(footprint.Kind))
                {
                    throw new InvalidOperationException(
                        "Home furniture kinds must be valid and unique.");
                }

                ValidateRect(
                    footprint.Bounds,
                    $"Home furniture '{footprint.Id}'");
                if (!Contains(
                        plan.InnerRoomBounds,
                        footprint.Bounds) ||
                    !IsPositiveFinite(footprint.Height) ||
                    footprint.Height > plan.RoomHeight + Tolerance)
                {
                    throw new InvalidOperationException(
                        $"Home furniture '{footprint.Id}' must fit " +
                        "inside the room.");
                }

                if (footprint.IsFixture)
                {
                    if (!Contains(
                            plan.BathroomBounds,
                            footprint.Bounds))
                    {
                        throw new InvalidOperationException(
                            $"Bathroom fixture '{footprint.Id}' must stay " +
                            "inside the bathroom.");
                    }
                }
                else if (footprint.Bounds.Overlaps(
                             plan.BathroomBounds,
                             true))
                {
                    throw new InvalidOperationException(
                        $"Living furniture '{footprint.Id}' cannot occupy " +
                        "the bathroom.");
                }

                if (footprint.Bounds.Overlaps(
                        plan.EntryCorridor,
                        true) ||
                    footprint.Bounds.Overlaps(
                        plan.BathroomDoorway,
                        true))
                {
                    throw new InvalidOperationException(
                        $"Home furniture '{footprint.Id}' must leave " +
                        "doorways clear.");
                }

                if (footprint.BlocksMovement)
                {
                    for (int pathIndex = 0;
                         pathIndex < plan.Paths.Count;
                         pathIndex++)
                    {
                        if (footprint.Bounds.Overlaps(
                                plan.Paths[pathIndex].Bounds,
                                true))
                        {
                            throw new InvalidOperationException(
                                $"Blocking furniture '{footprint.Id}' " +
                                $"intersects path " +
                                $"'{plan.Paths[pathIndex].Id}'.");
                        }
                    }
                }

                for (int other = index + 1;
                     other < plan.Furniture.Count;
                     other++)
                {
                    if (footprint.Bounds.Overlaps(
                            plan.Furniture[other].Bounds,
                            true))
                    {
                        throw new InvalidOperationException(
                            "Home furniture footprints cannot overlap.");
                    }
                }
            }

            EnsureRequiredKinds(
                kinds,
                RequiredFurnitureKinds,
                "home furniture");
        }

        private static void EnsurePathsConnected(
            IReadOnlyList<HomeInteriorPath> paths)
        {
            var visited = new bool[paths.Count];
            var pending = new Queue<int>();
            visited[0] = true;
            pending.Enqueue(0);
            int visitedCount = 0;
            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                visitedCount++;
                for (int index = 0;
                     index < paths.Count;
                     index++)
                {
                    if (visited[index] ||
                        !AreConnected(
                            paths[current].Bounds,
                            paths[index].Bounds))
                    {
                        continue;
                    }

                    visited[index] = true;
                    pending.Enqueue(index);
                }
            }

            if (visitedCount != paths.Count)
            {
                throw new InvalidOperationException(
                    "All reserved home paths must form one connected route.");
            }
        }

        private static bool AreConnected(
            Rect first,
            Rect second)
        {
            Rect expanded = Rect.MinMaxRect(
                first.xMin - Tolerance,
                first.yMin - Tolerance,
                first.xMax + Tolerance,
                first.yMax + Tolerance);
            return expanded.Overlaps(second, true);
        }

        private static void EnsureRequiredKinds<T>(
            HashSet<T> actual,
            IReadOnlyList<T> required,
            string label)
        {
            for (int index = 0; index < required.Count; index++)
            {
                if (!actual.Contains(required[index]))
                {
                    throw new InvalidOperationException(
                        $"The layout is missing required {label} " +
                        $"'{required[index]}'.");
                }
            }
        }

        private static void ValidateRect(
            Rect rect,
            string label)
        {
            if (!IsFinite(rect.x) ||
                !IsFinite(rect.y) ||
                !IsPositiveFinite(rect.width) ||
                !IsPositiveFinite(rect.height))
            {
                throw new InvalidOperationException(
                    $"{label} must have finite positive bounds.");
            }
        }

        private static bool Approximately(
            Rect first,
            Rect second)
        {
            return Mathf.Abs(first.xMin - second.xMin) <= Tolerance &&
                   Mathf.Abs(first.xMax - second.xMax) <= Tolerance &&
                   Mathf.Abs(first.yMin - second.yMin) <= Tolerance &&
                   Mathf.Abs(first.yMax - second.yMax) <= Tolerance;
        }

        private static bool Contains(
            Rect outer,
            Rect inner)
        {
            return inner.xMin >= outer.xMin - Tolerance &&
                   inner.xMax <= outer.xMax + Tolerance &&
                   inner.yMin >= outer.yMin - Tolerance &&
                   inner.yMax <= outer.yMax + Tolerance;
        }

        private static bool Contains(
            Rect bounds,
            Vector3 position,
            float radius)
        {
            return position.x >= bounds.xMin + radius - Tolerance &&
                   position.x <= bounds.xMax - radius + Tolerance &&
                   position.z >= bounds.yMin + radius - Tolerance &&
                   position.z <= bounds.yMax - radius + Tolerance;
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

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }
    }
}
