using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class BarInteriorLayoutValidator
    {
        public const float PlayerRadius = 0.32f;
        public const float MinimumPrimaryClearance = 2.4f;
        public const float MinimumSecondaryClearance = 1.2f;
        public const int MaximumNpcAnchors = 14;
        public const int MaximumLightAnchors = 6;

        private const float Tolerance = 0.001f;
        private const float PathConnectionTolerance = 0.01f;
        private const float MaximumCounterStationGap = 0.5f;

        public static void ValidateOrThrow(BarInteriorLayoutPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (BarDistrictIdentityCatalog.Normalize(plan.District) !=
                plan.District)
            {
                throw new InvalidOperationException(
                    "A bar interior must wear one of the four bar " +
                    $"district identities, got {plan.District}.");
            }

            ValidateRoom(plan);
            ValidateZones(plan);
            ValidatePaths(plan);
            ValidateFurniture(plan);
            ValidateNpcAnchors(plan);
            ValidateLightAnchors(plan);
            ValidateAudioAnchors(plan);
        }

        private static void ValidateRoom(BarInteriorLayoutPlan plan)
        {
            RequirePositiveFinite(plan.RoomSize.x, "Room width");
            RequirePositiveFinite(plan.RoomSize.y, "Room depth");
            RequirePositiveFinite(plan.RoomHeight, "Room height");
            RequirePositiveFinite(plan.WallThickness, "Wall thickness");
            if (plan.WallThickness * 2f >=
                    Mathf.Min(plan.RoomSize.x, plan.RoomSize.y))
            {
                throw new InvalidOperationException(
                    "Wall thickness leaves no usable room.");
            }

            RequireValidRect(plan.RoomBounds, "Room bounds");
            RequireValidRect(plan.WalkableBounds, "Walkable bounds");
            if (!Contains(plan.RoomBounds, plan.WalkableBounds))
            {
                throw new InvalidOperationException(
                    "Walkable bounds must stay inside the room.");
            }

            RequireSupportedActivity(plan.Activity);
            RequireFinite(plan.PlayerSpawn, "Player spawn");
            RequireFinite(plan.ExitPosition, "Exit position");
            RequireFinite(plan.ExitApproachPosition, "Exit approach");
            RequireFinite(
                plan.ActivityStationPosition,
                "Activity station");
            RequireFinite(
                plan.CounterStationPosition,
                "Counter station");
            RequirePositiveFinite(
                plan.ExitTriggerSize,
                "Exit trigger size");
            RequirePositiveFinite(
                plan.ActivityStationTriggerSize,
                "Activity station trigger size");
            RequirePositiveFinite(
                plan.CounterStationTriggerSize,
                "Counter station trigger size");
            RequirePositiveFinite(plan.CounterSize, "Counter size");

            RequireInsideWithRadius(
                plan.WalkableBounds,
                plan.PlayerSpawn,
                PlayerRadius,
                "Player spawn");
            RequireInsideWithRadius(
                plan.WalkableBounds,
                plan.ExitApproachPosition,
                PlayerRadius,
                "Exit approach");
            RequireInsideWithRadius(
                plan.WalkableBounds,
                plan.ActivityStationPosition,
                PlayerRadius,
                "Activity station");
            RequireInsideWithRadius(
                plan.WalkableBounds,
                plan.CounterStationPosition,
                PlayerRadius,
                "Counter station");
            Rect counterStationBounds = CreatePlanarBounds(
                plan.CounterStationPosition,
                plan.CounterStationTriggerSize);
            if (!Contains(plan.WalkableBounds, counterStationBounds))
            {
                throw new InvalidOperationException(
                    "Counter station trigger must stay inside the " +
                    "walkable bounds.");
            }

            RequireInsideRoom(plan, plan.ExitPosition, "Exit position");
            RequireInsideRoom(
                plan,
                plan.CounterPosition,
                "Counter position");
        }

        private static void ValidateZones(BarInteriorLayoutPlan plan)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new HashSet<BarInteriorZoneKind>();
            for (int index = 0; index < plan.Zones.Count; index++)
            {
                BarInteriorZone zone = plan.Zones[index];
                RequireUniqueId(zone.Id, ids, "zone");
                RequireValidRect(zone.Bounds, $"Zone '{zone.Id}'");
                if (!Contains(plan.RoomBounds, zone.Bounds))
                {
                    throw new InvalidOperationException(
                        $"Zone '{zone.Id}' leaves the room.");
                }

                if (!kinds.Add(zone.Kind))
                {
                    throw new InvalidOperationException(
                        $"Zone kind '{zone.Kind}' is duplicated.");
                }
            }

            foreach (BarInteriorZoneKind kind in
                     Enum.GetValues(typeof(BarInteriorZoneKind)))
            {
                if (!kinds.Contains(kind))
                {
                    throw new InvalidOperationException(
                        $"Required zone '{kind}' is missing.");
                }
            }
        }

        private static void ValidatePaths(BarInteriorLayoutPlan plan)
        {
            if (plan.Paths.Count < 2)
            {
                throw new InvalidOperationException(
                    "The layout requires primary and secondary paths.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new HashSet<BarInteriorPathKind>();
            int mainIndex = -1;
            for (int index = 0; index < plan.Paths.Count; index++)
            {
                BarInteriorPath path = plan.Paths[index];
                RequireUniqueId(path.Id, ids, "path");
                RequireValidRect(path.Bounds, $"Path '{path.Id}'");
                if (!Contains(plan.WalkableBounds, path.Bounds))
                {
                    throw new InvalidOperationException(
                        $"Path '{path.Id}' leaves the walkable bounds.");
                }

                if (!kinds.Add(path.Kind))
                {
                    throw new InvalidOperationException(
                        $"Path kind '{path.Kind}' is duplicated.");
                }

                float requiredClearance = path.IsPrimary
                    ? MinimumPrimaryClearance
                    : MinimumSecondaryClearance;
                RequirePositiveFinite(
                    path.Clearance,
                    $"Path '{path.Id}' clearance");
                if (path.Clearance + Tolerance < requiredClearance ||
                    Mathf.Min(
                        path.Bounds.width,
                        path.Bounds.height) +
                    Tolerance <
                    path.Clearance)
                {
                    throw new InvalidOperationException(
                        $"Path '{path.Id}' does not provide its required " +
                        $"{requiredClearance:0.0} m clearance.");
                }

                if (path.IsPrimary)
                {
                    mainIndex = index;
                }
            }

            if (mainIndex < 0)
            {
                throw new InvalidOperationException(
                    "The layout has no primary path.");
            }

            EnsureEveryPathConnected(plan.Paths, mainIndex);
            RequirePointOnPath(
                plan.Paths,
                plan.PlayerSpawn,
                "Player spawn");
            RequirePointOnPath(
                plan.Paths,
                plan.ExitApproachPosition,
                "Exit approach");
            RequirePointOnPath(
                plan.Paths,
                plan.ActivityStationPosition,
                "Activity station");
            RequirePointOnPath(
                plan.Paths,
                plan.CounterStationPosition,
                "Counter station");
        }

        private static void EnsureEveryPathConnected(
            IReadOnlyList<BarInteriorPath> paths,
            int startIndex)
        {
            var visited = new bool[paths.Count];
            var pending = new Queue<int>();
            visited[startIndex] = true;
            pending.Enqueue(startIndex);
            int visitedCount = 0;
            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                visitedCount++;
                for (int index = 0; index < paths.Count; index++)
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
                    "Every path must connect to the primary circulation.");
            }
        }

        private static void ValidateFurniture(
            BarInteriorLayoutPlan plan)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int counterCount = 0;
            int stageCount = 0;
            int activityFixtureCount = 0;
            BarInteriorFurnitureFootprint counter = default;
            for (int index = 0;
                 index < plan.FurnitureFootprints.Count;
                 index++)
            {
                BarInteriorFurnitureFootprint footprint =
                    plan.FurnitureFootprints[index];
                RequireUniqueId(footprint.Id, ids, "furniture footprint");
                RequireValidRect(
                    footprint.Bounds,
                    $"Furniture '{footprint.Id}'");
                RequirePositiveFinite(
                    footprint.Height,
                    $"Furniture '{footprint.Id}' height");
                if (footprint.Height > plan.RoomHeight + Tolerance ||
                    !Contains(plan.RoomBounds, footprint.Bounds))
                {
                    throw new InvalidOperationException(
                        $"Furniture '{footprint.Id}' does not fit the room.");
                }

                if (footprint.Kind ==
                    BarInteriorFurnitureKind.Counter)
                {
                    counter = footprint;
                    counterCount++;
                }
                else if (footprint.Kind ==
                         BarInteriorFurnitureKind.Stage)
                {
                    stageCount++;
                }
                else if (footprint.Kind ==
                         BarInteriorFurnitureKind.ActivityFixture)
                {
                    activityFixtureCount++;
                    if (footprint.Activity != plan.Activity)
                    {
                        throw new InvalidOperationException(
                            "The activity fixture does not match the plan.");
                    }
                }

                if (!footprint.BlocksMovement)
                {
                    continue;
                }

                for (int pathIndex = 0;
                     pathIndex < plan.Paths.Count;
                     pathIndex++)
                {
                    if (footprint.Bounds.Overlaps(
                            plan.Paths[pathIndex].Bounds))
                    {
                        throw new InvalidOperationException(
                            $"Furniture '{footprint.Id}' blocks path " +
                            $"'{plan.Paths[pathIndex].Id}'.");
                    }
                }
            }

            if (counterCount != 1 ||
                stageCount != 1 ||
                activityFixtureCount != 1)
            {
                throw new InvalidOperationException(
                    "The plan requires one counter, stage and activity " +
                    "fixture.");
            }

            ValidateCounterContract(plan, counter);
            ValidateCounterStationContract(plan, counter);
            ValidateFurnitureSeparation(plan.FurnitureFootprints);
        }

        private static void ValidateCounterContract(
            BarInteriorLayoutPlan plan,
            BarInteriorFurnitureFootprint counter)
        {
            Vector2 expectedCenter =
                new Vector2(
                    plan.CounterPosition.x,
                    plan.CounterPosition.z);
            if ((counter.Bounds.center - expectedCenter).sqrMagnitude >
                    Tolerance * Tolerance ||
                Mathf.Abs(
                    counter.Bounds.width -
                    plan.CounterSize.x) >
                    Tolerance ||
                Mathf.Abs(
                    counter.Bounds.height -
                    plan.CounterSize.z) >
                    Tolerance ||
                Mathf.Abs(counter.Height - plan.CounterSize.y) >
                    Tolerance)
            {
                throw new InvalidOperationException(
                    "Counter position/size must match its footprint.");
            }
        }

        private static void ValidateCounterStationContract(
            BarInteriorLayoutPlan plan,
            BarInteriorFurnitureFootprint counter)
        {
            Rect stationBounds = CreatePlanarBounds(
                plan.CounterStationPosition,
                plan.CounterStationTriggerSize);
            if (stationBounds.xMin < counter.Bounds.xMin - Tolerance ||
                stationBounds.xMax > counter.Bounds.xMax + Tolerance)
            {
                throw new InvalidOperationException(
                    "Counter station must stay horizontally in front of " +
                    "the counter.");
            }

            float counterGap = counter.Bounds.yMin - stationBounds.yMax;
            if (counterGap < -Tolerance ||
                counterGap > MaximumCounterStationGap + Tolerance)
            {
                throw new InvalidOperationException(
                    "Counter station must stay directly in front of the " +
                    "counter without intersecting it.");
            }

            for (int index = 0;
                 index < plan.FurnitureFootprints.Count;
                 index++)
            {
                BarInteriorFurnitureFootprint footprint =
                    plan.FurnitureFootprints[index];
                if (footprint.BlocksMovement &&
                    stationBounds.Overlaps(footprint.Bounds))
                {
                    throw new InvalidOperationException(
                        "Counter station trigger intersects furniture " +
                        $"'{footprint.Id}'.");
                }
            }

            Rect activityBounds = CreatePlanarBounds(
                plan.ActivityStationPosition,
                plan.ActivityStationTriggerSize);
            Rect exitBounds = CreatePlanarBounds(
                plan.ExitPosition,
                plan.ExitTriggerSize);
            if (stationBounds.Overlaps(activityBounds) ||
                stationBounds.Overlaps(exitBounds))
            {
                throw new InvalidOperationException(
                    "Counter station trigger intersects another " +
                    "interaction trigger.");
            }
        }

        private static void ValidateFurnitureSeparation(
            IReadOnlyList<BarInteriorFurnitureFootprint> furniture)
        {
            for (int first = 0; first < furniture.Count; first++)
            {
                if (!furniture[first].BlocksMovement)
                {
                    continue;
                }

                for (int second = first + 1;
                     second < furniture.Count;
                     second++)
                {
                    if (furniture[second].BlocksMovement &&
                        furniture[first].Bounds.Overlaps(
                            furniture[second].Bounds) &&
                        !IsCounterAssemblyPair(
                            furniture[first],
                            furniture[second]))
                    {
                        throw new InvalidOperationException(
                            $"Furniture '{furniture[first].Id}' overlaps " +
                            $"'{furniture[second].Id}'.");
                    }
                }
            }
        }

        private static bool IsCounterAssemblyPair(
            BarInteriorFurnitureFootprint first,
            BarInteriorFurnitureFootprint second)
        {
            return (first.Kind == BarInteriorFurnitureKind.Counter &&
                    second.Kind == BarInteriorFurnitureKind.CounterReturn) ||
                   (first.Kind == BarInteriorFurnitureKind.CounterReturn &&
                    second.Kind == BarInteriorFurnitureKind.Counter);
        }

        private static void ValidateNpcAnchors(
            BarInteriorLayoutPlan plan)
        {
            if (plan.NpcAnchors.Count == 0 ||
                plan.NpcAnchors.Count > MaximumNpcAnchors)
            {
                throw new InvalidOperationException(
                    $"The layout requires 1-{MaximumNpcAnchors} NPC " +
                    "anchors.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < plan.NpcAnchors.Count; index++)
            {
                BarNpcAnchor anchor = plan.NpcAnchors[index];
                RequireUniqueId(anchor.Id, ids, "NPC anchor");
                RequireInsideRoom(plan, anchor.Position, $"NPC '{anchor.Id}'");
                RequireFinite(anchor.YawDegrees, $"NPC '{anchor.Id}' yaw");
                if (anchor.VisualVariant < 0 ||
                    !IsFinite(anchor.AnimationPhase) ||
                    anchor.AnimationPhase < 0f ||
                    anchor.AnimationPhase >= 1f)
                {
                    throw new InvalidOperationException(
                        $"NPC '{anchor.Id}' has invalid presentation data.");
                }

                if (anchor.IsMobile)
                {
                    RequirePointOnPath(
                        plan.Paths,
                        anchor.Position,
                        $"Mobile NPC '{anchor.Id}'");
                    continue;
                }

                if (anchor.Role == BarNpcRole.SeatedPatron ||
                    anchor.Role == BarNpcRole.Performer)
                {
                    continue;
                }

                for (int furnitureIndex = 0;
                     furnitureIndex <
                     plan.FurnitureFootprints.Count;
                     furnitureIndex++)
                {
                    BarInteriorFurnitureFootprint footprint =
                        plan.FurnitureFootprints[furnitureIndex];
                    if (footprint.BlocksMovement &&
                        Contains(footprint.Bounds, anchor.Position))
                    {
                        throw new InvalidOperationException(
                            $"NPC '{anchor.Id}' intersects furniture " +
                            $"'{footprint.Id}'.");
                    }
                }
            }
        }

        private static void ValidateLightAnchors(
            BarInteriorLayoutPlan plan)
        {
            if (plan.LightAnchors.Count == 0 ||
                plan.LightAnchors.Count > MaximumLightAnchors)
            {
                throw new InvalidOperationException(
                    $"The layout requires 1-{MaximumLightAnchors} light " +
                    "anchors.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0;
                 index < plan.LightAnchors.Count;
                 index++)
            {
                BarInteriorLightAnchor anchor =
                    plan.LightAnchors[index];
                RequireUniqueId(anchor.Id, ids, "light anchor");
                RequireInsideRoom(
                    plan,
                    anchor.Position,
                    $"Light '{anchor.Id}'");
                RequireFinite(
                    anchor.Direction,
                    $"Light '{anchor.Id}' direction");
                if (anchor.Direction.sqrMagnitude < 0.5f)
                {
                    throw new InvalidOperationException(
                        $"Light '{anchor.Id}' has no usable direction.");
                }

                RequireColor(anchor.Color, $"Light '{anchor.Id}' color");
                RequirePositiveFinite(
                    anchor.Intensity,
                    $"Light '{anchor.Id}' intensity");
                RequirePositiveFinite(
                    anchor.Range,
                    $"Light '{anchor.Id}' range");
                if (!IsFinite(anchor.SpotAngle) ||
                    anchor.SpotAngle <= 0f ||
                    anchor.SpotAngle >= 180f)
                {
                    throw new InvalidOperationException(
                        $"Light '{anchor.Id}' has an invalid spot angle.");
                }
            }
        }

        private static void ValidateAudioAnchors(
            BarInteriorLayoutPlan plan)
        {
            if (plan.AudioAnchors.Count == 0)
            {
                throw new InvalidOperationException(
                    "The layout requires audio anchors.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0;
                 index < plan.AudioAnchors.Count;
                 index++)
            {
                BarInteriorAudioAnchor anchor =
                    plan.AudioAnchors[index];
                RequireUniqueId(anchor.Id, ids, "audio anchor");
                RequireInsideRoom(
                    plan,
                    anchor.Position,
                    $"Audio '{anchor.Id}'");
                RequirePositiveFinite(
                    anchor.Radius,
                    $"Audio '{anchor.Id}' radius");
                if (!IsFinite(anchor.Gain) ||
                    anchor.Gain < 0f ||
                    anchor.Gain > 1f)
                {
                    throw new InvalidOperationException(
                        $"Audio '{anchor.Id}' has invalid gain.");
                }
            }
        }

        private static void RequireSupportedActivity(
            BarActivityKind activity)
        {
            if (activity != BarActivityKind.Cocktail &&
                activity != BarActivityKind.BeerPong &&
                activity != BarActivityKind.SplitTheG &&
                activity != BarActivityKind.TinctureMatch)
            {
                throw new InvalidOperationException(
                    $"Unsupported bar activity '{activity}'.");
            }
        }

        private static void RequireInsideRoom(
            BarInteriorLayoutPlan plan,
            Vector3 position,
            string label)
        {
            RequireFinite(position, label);
            if (!Contains(plan.RoomBounds, position) ||
                position.y < -Tolerance ||
                position.y > plan.RoomHeight + Tolerance)
            {
                throw new InvalidOperationException(
                    $"{label} leaves the room.");
            }
        }

        private static void RequireInsideWithRadius(
            Rect bounds,
            Vector3 position,
            float radius,
            string label)
        {
            if (position.x < bounds.xMin + radius - Tolerance ||
                position.x > bounds.xMax - radius + Tolerance ||
                position.z < bounds.yMin + radius - Tolerance ||
                position.z > bounds.yMax - radius + Tolerance)
            {
                throw new InvalidOperationException(
                    $"{label} lacks player-radius clearance.");
            }
        }

        private static void RequirePointOnPath(
            IReadOnlyList<BarInteriorPath> paths,
            Vector3 position,
            string label)
        {
            for (int index = 0; index < paths.Count; index++)
            {
                if (Contains(paths[index].Bounds, position))
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"{label} is not connected to a reserved path.");
        }

        private static bool AreConnected(Rect first, Rect second)
        {
            Rect expanded = Rect.MinMaxRect(
                first.xMin - PathConnectionTolerance,
                first.yMin - PathConnectionTolerance,
                first.xMax + PathConnectionTolerance,
                first.yMax + PathConnectionTolerance);
            return expanded.Overlaps(second) ||
                   Contains(expanded, second.min) ||
                   Contains(expanded, second.max);
        }

        private static void RequireUniqueId(
            string id,
            HashSet<string> ids,
            string kind)
        {
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
            {
                throw new InvalidOperationException(
                    $"Every {kind} requires a unique non-empty ID.");
            }
        }

        private static void RequireValidRect(Rect bounds, string label)
        {
            if (!IsFinite(bounds.x) ||
                !IsFinite(bounds.y) ||
                !IsFinite(bounds.width) ||
                !IsFinite(bounds.height) ||
                bounds.width <= 0f ||
                bounds.height <= 0f)
            {
                throw new InvalidOperationException(
                    $"{label} must be a finite positive rectangle.");
            }
        }

        private static Rect CreatePlanarBounds(
            Vector3 position,
            Vector3 size)
        {
            return new Rect(
                position.x - size.x * 0.5f,
                position.z - size.z * 0.5f,
                size.x,
                size.z);
        }

        private static void RequirePositiveFinite(
            Vector3 value,
            string label)
        {
            if (!IsFinite(value.x) ||
                !IsFinite(value.y) ||
                !IsFinite(value.z) ||
                value.x <= 0f ||
                value.y <= 0f ||
                value.z <= 0f)
            {
                throw new InvalidOperationException(
                    $"{label} must be finite and positive.");
            }
        }

        private static void RequirePositiveFinite(
            float value,
            string label)
        {
            if (!IsFinite(value) || value <= 0f)
            {
                throw new InvalidOperationException(
                    $"{label} must be finite and positive.");
            }
        }

        private static void RequireFinite(Vector3 value, string label)
        {
            if (!IsFinite(value.x) ||
                !IsFinite(value.y) ||
                !IsFinite(value.z))
            {
                throw new InvalidOperationException(
                    $"{label} must be finite.");
            }
        }

        private static void RequireFinite(float value, string label)
        {
            if (!IsFinite(value))
            {
                throw new InvalidOperationException(
                    $"{label} must be finite.");
            }
        }

        private static void RequireColor(Color color, string label)
        {
            if (!IsFinite(color.r) ||
                !IsFinite(color.g) ||
                !IsFinite(color.b) ||
                !IsFinite(color.a) ||
                color.r < 0f ||
                color.g < 0f ||
                color.b < 0f ||
                color.a < 0f)
            {
                throw new InvalidOperationException(
                    $"{label} must be finite and non-negative.");
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
                   point.z <= bounds.yMax + Tolerance;
        }

        private static bool Contains(Rect bounds, Vector2 point)
        {
            return point.x >= bounds.xMin - Tolerance &&
                   point.x <= bounds.xMax + Tolerance &&
                   point.y >= bounds.yMin - Tolerance &&
                   point.y <= bounds.yMax + Tolerance;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
