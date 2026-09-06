using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Why a visible track leaves the village lane. Its purpose controls only
    /// width and wear; navigation and rendering both read the same segment.
    /// </summary>
    public enum AlpineVillagePathKind
    {
        StationExit = 0,
        HouseThreshold = 1,
        SummitThreshold = 2,
        ChapelSpur = 3,

        // `4` and `5` were the adit and cemetery spurs. Both places are gone
        // from the village and from the story; the numbers stay as holes.
        ChapelSource = 6,

        /// <summary>The worn line out to the spring's head.</summary>
        SpringSpur = 7
    }

    /// <summary>
    /// One straight part of a trodden village path. Landmark routes may use
    /// several parts around real footprints; household paths need only one.
    /// </summary>
    public readonly struct AlpineVillagePathDescriptor
    {
        internal AlpineVillagePathDescriptor(
            string stableId,
            string ownerPlotStableId,
            AlpineVillagePathKind kind,
            Vector3 start,
            Vector3 end,
            float surfaceHalfWidth,
            float walkableHalfWidth)
        {
            StableId = stableId ?? string.Empty;
            OwnerPlotStableId = ownerPlotStableId ?? string.Empty;
            Kind = kind;
            Start = start;
            End = end;
            SurfaceHalfWidth = surfaceHalfWidth;
            WalkableHalfWidth = walkableHalfWidth;
        }

        public string StableId { get; }
        public string OwnerPlotStableId { get; }
        public AlpineVillagePathKind Kind { get; }
        public Vector3 Start { get; }
        public Vector3 End { get; }

        /// <summary>The compacted strip the player can actually see.</summary>
        public float SurfaceHalfWidth { get; }

        /// <summary>
        /// The traversal corridor around that strip: how much room a person
        /// walking this route actually needs.
        ///
        /// It stopped being the walkable mask's own capsule when the mask was
        /// opened to the whole bowl - ground is walkable by default now and a
        /// path is a route rather than a permission. What it still is, and
        /// what the validator reads it as, is the route's CLEARANCE ENVELOPE:
        /// no part of it may enter a rotated plot footprint, so a visible
        /// track can never be drawn into a wall.
        /// </summary>
        public float WalkableHalfWidth { get; }

        public float LengthXZ => Vector2.Distance(ToXZ(Start), ToXZ(End));

        public float DistanceToCenterline(Vector2 point)
        {
            Vector2 start = ToXZ(Start);
            Vector2 segment = ToXZ(End) - start;
            float lengthSquared = segment.sqrMagnitude;
            float amount = lengthSquared <= 0.000001f
                ? 0f
                : Mathf.Clamp01(
                    Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * amount);
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }
    }

    /// <summary>
    /// Derives every visible track from the validated village plan.
    ///
    /// These were once the walkable mask itself - a capsule per path, and
    /// nothing else in the village standable - on the argument that an
    /// invisible twenty-metre branch through pristine snow is not a route.
    /// The cost of that was six per cent of the bowl walkable and an
    /// invisible wall a step off the lane in every direction, so the mask is
    /// the bowl now and these are what they look like: the compacted routes
    /// between the places worth going, showing where people habitually walk
    /// rather than where they are permitted to.
    /// </summary>
    public static class AlpineVillagePathPlanner
    {
        public const float HouseholdSurfaceHalfWidth = 0.62f;
        public const float SummitSurfaceHalfWidth = 0.82f;
        public const float LandmarkSurfaceHalfWidth = 0.88f;
        public const float SourceSurfaceHalfWidth = 0.70f;
        public const float StationSurfaceHalfWidth = 1.25f;
        public const float BranchWalkableHalfWidth = 1.1f;
        public const float SourceWalkableHalfWidth = 1.05f;
        public const float StationWalkableHalfWidth = 1.6f;

        /// <summary>
        /// How far past the compacted ribbon the ground is still bare soil
        /// before the snow starts fading back in. The world builder paints
        /// the terrain with it, and the path contract is judged against it:
        /// a hero whose centre stays inside the ribbon plus this skirt is
        /// standing on trodden ground, not on pristine snow. The two must
        /// read one constant, or the mask and the paint drift apart again.
        /// </summary>
        public const float BareSkirtHalfWidth = 0.15f;

        // The spring's head stands where the adit used to, behind the rear-row
        // house at beat 08, and a straight line to it cuts a frontage house on
        // every seeded layout - which is why the adit had this hook and why
        // the spring inherits it rather than being moved somewhere easier.
        // The route enters from above house 10, follows the outside of both,
        // then turns around house 08's seeded OBB.
        public const float SpringBypassLaneDistance = 78f;
        public const float SpringBypassOutwardDistance = 22f;
        public const float SpringBypassSafetyMargin = 0.04f;
        public const string SpringBlockingHouseStableId =
            "village-house-08";

        /// <summary>
        /// How far the point lies outside the nearest trodden surface - the
        /// lane skin or any path ribbon - and which way is out of it.
        /// Negative on trodden ground.
        ///
        /// It is a MINIMUM over every route, and that is the entire reason it
        /// lives in one place rather than inside a per-segment loop: lying
        /// snow beside one path has to disappear where another crosses it,
        /// and a measure that only knows its own segment cannot see the
        /// crossing. Every reader of "how far from trodden ground is this"
        /// asks here.
        /// </summary>
        public static float MeasureDistanceOutsideTrodden(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            Vector2 point,
            out Vector2 outward)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            float along = plan.Lane.FindNearest(point, out float lateral);
            AlpineVillageLaneSample sample = plan.Lane.Sample(along);
            var nearest = new Vector2(
                sample.Position.x,
                sample.Position.z);
            float best = lateral - sample.Width * 0.5f;

            for (int index = 0; index < paths.Count; index++)
            {
                AlpineVillagePathDescriptor path = paths[index];
                Vector2 closest = ClosestPointOnPath(path, point);
                float outside = Vector2.Distance(point, closest) -
                                path.SurfaceHalfWidth;
                if (outside >= best)
                {
                    continue;
                }

                best = outside;
                nearest = closest;
            }

            Vector2 delta = point - nearest;
            outward = delta.sqrMagnitude <= 0.000001f
                ? Vector2.up
                : delta.normalized;
            return best;
        }

        private static Vector2 ClosestPointOnPath(
            AlpineVillagePathDescriptor path,
            Vector2 point)
        {
            var start = new Vector2(path.Start.x, path.Start.z);
            Vector2 segment = new Vector2(path.End.x, path.End.z) - start;
            float lengthSquared = segment.sqrMagnitude;
            float amount = lengthSquared <= 0.000001f
                ? 0f
                : Mathf.Clamp01(
                    Vector2.Dot(point - start, segment) / lengthSquared);
            return start + segment * amount;
        }

        public static IReadOnlyList<AlpineVillagePathDescriptor> Create(
            AlpineVillagePlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var result = new List<AlpineVillagePathDescriptor>(
                plan.Plots.Count + 4);
            AppendStationExit(plan, result);
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AppendPlotPath(plan, plan.Plots[index], result);
            }

            AlpineVillagePathValidator.ValidateOrThrow(plan, result);
            return result;
        }

        private static void AppendStationExit(
            AlpineVillagePlan plan,
            ICollection<AlpineVillagePathDescriptor> target)
        {
            MountainRoadCablewayPlan cableway = plan.Station.Cableway;
            Vector3 stepFoot = plan.Station.PadArea.Center +
                               cableway.LineRight *
                               cableway.BoardingPlatformCenterOffset +
                               cableway.LineForward *
                               cableway.BoardingFenceForward;
            Add(
                target,
                "village-path-station-exit",
                string.Empty,
                AlpineVillagePathKind.StationExit,
                stepFoot,
                plan.Lane.Start,
                StationSurfaceHalfWidth,
                StationWalkableHalfWidth);
        }

        private static void AppendPlotPath(
            AlpineVillagePlan plan,
            AlpineVillagePlotDescriptor plot,
            ICollection<AlpineVillagePathDescriptor> target)
        {
            AlpineVillageLaneSample lane = plan.Lane.Sample(
                plot.LaneDistance);
            AlpineVillagePathKind kind = ResolveKind(plot.Kind);
            float visibleWidth = ResolveSurfaceHalfWidth(kind);

            if (plot.Kind == AlpineVillagePlotKind.House ||
                plot.Kind == AlpineVillagePlotKind.MothersHouse)
            {
                Add(
                    target,
                    $"{plot.StableId}-path",
                    plot.StableId,
                    kind,
                    lane.Position,
                    plot.DoorDockPosition,
                    visibleWidth,
                    BranchWalkableHalfWidth);
                return;
            }

            if (plot.Kind == AlpineVillagePlotKind.Spring)
            {
                AppendSpringBypassPath(plan, plot, target);
                return;
            }

            // The chapel takes one shallow found turn and that is the whole
            // of it.
            float bendOffset = plot.Kind == AlpineVillagePlotKind.Chapel
                ? 1.35f
                : 0f;
            Vector3 middle = Vector3.Lerp(
                lane.Position,
                plot.DoorDockPosition,
                0.52f) + lane.Forward * bendOffset;
            middle.y = AlpineVillageTerrainSampler.SampleHeight(
                plan,
                new Vector2(middle.x, middle.z));

            Add(
                target,
                $"{plot.StableId}-path-a",
                plot.StableId,
                kind,
                lane.Position,
                middle,
                visibleWidth,
                BranchWalkableHalfWidth);
            Add(
                target,
                $"{plot.StableId}-path-b",
                plot.StableId,
                kind,
                middle,
                plot.DoorDockPosition,
                visibleWidth,
                BranchWalkableHalfWidth);

            if (plot.Kind == AlpineVillagePlotKind.Chapel)
            {
                AppendChapelSourcePath(plan, plot, target);
            }
        }

        private static void AppendSpringBypassPath(
            AlpineVillagePlan plan,
            AlpineVillagePlotDescriptor spring,
            ICollection<AlpineVillagePathDescriptor> target)
        {
            AlpineVillageLaneSample entry = plan.Lane.Sample(
                SpringBypassLaneDistance);
            Vector3 outside = entry.Position +
                              entry.Right *
                              (spring.Side * SpringBypassOutwardDistance);
            Ground(plan, ref outside);

            IReadOnlyList<Vector3> route = ResolveSpringBypass(
                plan,
                entry.Position,
                plan.Brook.ApproachPosition,
                outside);
            for (int index = 0; index < route.Count - 1; index++)
            {
                Add(
                    target,
                    $"{spring.StableId}-path-{(char)('a' + index)}",
                    spring.StableId,
                    AlpineVillagePathKind.SpringSpur,
                    route[index],
                    route[index + 1],
                    LandmarkSurfaceHalfWidth,
                    BranchWalkableHalfWidth);
            }
        }

        private static IReadOnlyList<Vector3> ResolveSpringBypass(
            AlpineVillagePlan plan,
            Vector3 start,
            Vector3 approach,
            Vector3 outside)
        {
            if (SpringSegmentIsClear(plan, start, approach))
            {
                return new[] { start, approach };
            }

            AlpineVillagePlotDescriptor blocker = null;
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                if (string.Equals(
                        plan.Plots[index].StableId,
                        SpringBlockingHouseStableId,
                        StringComparison.Ordinal))
                {
                    blocker = plan.Plots[index];
                    break;
                }
            }

            if (blocker == null)
            {
                throw new InvalidOperationException(
                    $"The spring bypass cannot find " +
                    $"'{SpringBlockingHouseStableId}'.");
            }

            float envelope = Mathf.Max(
                LandmarkSurfaceHalfWidth,
                BranchWalkableHalfWidth);
            Vector3 forward = blocker.Facing.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float halfWidth = blocker.FootprintSize.x * 0.5f +
                              envelope + SpringBypassSafetyMargin;
            float halfDepth = blocker.FootprintSize.y * 0.5f +
                              envelope + SpringBypassSafetyMargin;
            Vector3[] corners =
            {
                blocker.GroundCenter + right * halfWidth +
                    forward * halfDepth,
                blocker.GroundCenter + right * halfWidth -
                    forward * halfDepth,
                blocker.GroundCenter - right * halfWidth +
                    forward * halfDepth,
                blocker.GroundCenter - right * halfWidth -
                    forward * halfDepth
            };
            for (int index = 0; index < corners.Length; index++)
            {
                Ground(plan, ref corners[index]);
            }

            // Keep the outer clearing as a fallback, not a mandatory detour.
            // This small visibility graph checks every edge against all houses;
            // its stable node order also resolves equal-length routes consistently.
            Vector3[] nodes =
            {
                start,
                approach,
                corners[0],
                corners[1],
                corners[2],
                corners[3],
                outside
            };
            var edges = new float[nodes.Length, nodes.Length];
            for (int first = 0; first < nodes.Length; first++)
            {
                for (int second = first + 1; second < nodes.Length; second++)
                {
                    float length = SpringSegmentIsClear(
                        plan, nodes[first], nodes[second])
                        ? DistanceXZ(nodes[first], nodes[second])
                        : float.PositiveInfinity;
                    edges[first, second] = length;
                    edges[second, first] = length;
                }
            }

            var distances = new float[nodes.Length];
            var previous = new int[nodes.Length];
            var visited = new bool[nodes.Length];
            for (int index = 0; index < nodes.Length; index++)
            {
                distances[index] = float.PositiveInfinity;
                previous[index] = -1;
            }

            distances[0] = 0f;
            for (int step = 0; step < nodes.Length; step++)
            {
                int current = -1;
                float nearest = float.PositiveInfinity;
                for (int index = 0; index < nodes.Length; index++)
                {
                    if (!visited[index] && distances[index] < nearest)
                    {
                        current = index;
                        nearest = distances[index];
                    }
                }

                if (current < 0)
                {
                    break;
                }

                if (current == 1)
                {
                    var route = new List<Vector3>();
                    for (int index = current; index >= 0; index = previous[index])
                    {
                        route.Add(nodes[index]);
                    }

                    route.Reverse();
                    return route;
                }

                visited[current] = true;
                for (int next = 0; next < nodes.Length; next++)
                {
                    float candidate = nearest + edges[current, next];
                    if (!visited[next] && candidate < distances[next])
                    {
                        distances[next] = candidate;
                        previous[next] = current;
                    }
                }
            }

            throw new InvalidOperationException(
                "The spring approach cannot clear the seeded houses.");
        }

        private static bool SpringSegmentIsClear(
            AlpineVillagePlan plan,
            Vector3 start,
            Vector3 end)
        {
            return AlpineVillagePathValidator.SegmentClearsAllFootprints(
                plan,
                start,
                end,
                Mathf.Max(
                    LandmarkSurfaceHalfWidth,
                    BranchWalkableHalfWidth));
        }

        private static float DistanceXZ(Vector3 first, Vector3 second)
        {
            return Vector2.Distance(
                new Vector2(first.x, first.z),
                new Vector2(second.x, second.z));
        }

        /// <summary>
        /// The ordinary catch basin stands under the chapel's rear pipe. A
        /// narrow worn turn around the wall makes that visible cause reachable
        /// without turning the chapel into a new route through the village.
        /// </summary>
        private static void AppendChapelSourcePath(
            AlpineVillagePlan plan,
            AlpineVillagePlotDescriptor chapel,
            ICollection<AlpineVillagePathDescriptor> target)
        {
            Vector3 approach = GetChapelSourceApproachPosition(
                plan,
                chapel);
            Vector3 across = Vector3.Cross(
                Vector3.up,
                chapel.Facing).normalized;
            float sideReach = chapel.FootprintSize.x * 0.5f + 1.0f;
            Vector3 frontCorner = chapel.DoorDockPosition +
                                  across * sideReach;
            Vector3 rearCorner = approach + across * sideReach;
            Ground(plan, ref frontCorner);
            Ground(plan, ref rearCorner);

            Add(
                target,
                "village-chapel-source-path-a",
                chapel.StableId,
                AlpineVillagePathKind.ChapelSource,
                chapel.DoorDockPosition,
                frontCorner,
                SourceSurfaceHalfWidth,
                SourceWalkableHalfWidth);
            Add(
                target,
                "village-chapel-source-path-b",
                chapel.StableId,
                AlpineVillagePathKind.ChapelSource,
                frontCorner,
                rearCorner,
                SourceSurfaceHalfWidth,
                SourceWalkableHalfWidth);
            Add(
                target,
                "village-chapel-source-path-c",
                chapel.StableId,
                AlpineVillagePathKind.ChapelSource,
                rearCorner,
                approach,
                SourceSurfaceHalfWidth,
                SourceWalkableHalfWidth);
        }

        public static Vector3 GetChapelSourceBowlPosition(
            AlpineVillagePlan plan,
            AlpineVillagePlotDescriptor chapel)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (chapel == null ||
                chapel.Kind != AlpineVillagePlotKind.Chapel)
            {
                throw new ArgumentException(
                    "A chapel source needs the chapel plot.",
                    nameof(chapel));
            }

            Vector3 position = chapel.GroundCenter -
                               chapel.Facing *
                               (chapel.FootprintSize.y * 0.5f +
                                SourceWalkableHalfWidth + 0.08f);
            Ground(plan, ref position);
            return position;
        }

        public static Vector3 GetChapelSourceApproachPosition(
            AlpineVillagePlan plan,
            AlpineVillagePlotDescriptor chapel)
        {
            Vector3 bowl = GetChapelSourceBowlPosition(plan, chapel);
            Vector3 across = Vector3.Cross(
                Vector3.up,
                chapel.Facing).normalized;
            Vector3 position = bowl +
                across *
                (0.575f +
                 CityGroundTraversalPlanner.MaximumAgentRadius +
                 0.08f);
            Ground(plan, ref position);
            return position;
        }

        private static AlpineVillagePathKind ResolveKind(
            AlpineVillagePlotKind kind)
        {
            switch (kind)
            {
                case AlpineVillagePlotKind.House:
                    return AlpineVillagePathKind.HouseThreshold;
                case AlpineVillagePlotKind.MothersHouse:
                    return AlpineVillagePathKind.SummitThreshold;
                case AlpineVillagePlotKind.Chapel:
                    return AlpineVillagePathKind.ChapelSpur;
                case AlpineVillagePlotKind.Spring:
                    return AlpineVillagePathKind.SpringSpur;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static float ResolveSurfaceHalfWidth(
            AlpineVillagePathKind kind)
        {
            switch (kind)
            {
                case AlpineVillagePathKind.HouseThreshold:
                    return HouseholdSurfaceHalfWidth;
                case AlpineVillagePathKind.SummitThreshold:
                    return SummitSurfaceHalfWidth;
                case AlpineVillagePathKind.ChapelSpur:
                case AlpineVillagePathKind.SpringSpur:
                    return LandmarkSurfaceHalfWidth;
                case AlpineVillagePathKind.ChapelSource:
                    return SourceSurfaceHalfWidth;
                default:
                    return StationSurfaceHalfWidth;
            }
        }

        private static void Add(
            ICollection<AlpineVillagePathDescriptor> target,
            string stableId,
            string ownerPlotStableId,
            AlpineVillagePathKind kind,
            Vector3 start,
            Vector3 end,
            float surfaceHalfWidth,
            float walkableHalfWidth)
        {
            target.Add(new AlpineVillagePathDescriptor(
                stableId,
                ownerPlotStableId,
                kind,
                start,
                end,
                surfaceHalfWidth,
                walkableHalfWidth));
        }

        private static void Ground(
            AlpineVillagePlan plan,
            ref Vector3 position)
        {
            position.y = AlpineVillageTerrainSampler.SampleHeight(
                plan,
                new Vector2(position.x, position.z));
        }
    }

    /// <summary>
    /// Pure physical invariant for the visible and traversable path envelope.
    /// A path owns two widths, but both describe the same piece of ground, so
    /// its collision capsule uses the larger one. The owning threshold may be
    /// tangent at its endpoint; no segment may enter any plot footprint.
    /// </summary>
    public static class AlpineVillagePathValidator
    {
        private const float GeometryTolerance = 0.001f;

        public static void ValidateOrThrow(
            AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            var stableIds = new HashSet<string>(StringComparer.Ordinal);
            for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
            {
                AlpineVillagePathDescriptor path = paths[pathIndex];
                if (string.IsNullOrWhiteSpace(path.StableId) ||
                    !stableIds.Add(path.StableId) ||
                    path.LengthXZ <= 0.25f ||
                    path.SurfaceHalfWidth <= 0f ||
                    path.WalkableHalfWidth <= 0f)
                {
                    throw new InvalidOperationException(
                        $"Village path '{path.StableId}' is invalid.");
                }

                float envelope = Mathf.Max(
                    path.SurfaceHalfWidth,
                    path.WalkableHalfWidth);
                for (int plotIndex = 0;
                     plotIndex < plan.Plots.Count;
                     plotIndex++)
                {
                    AlpineVillagePlotDescriptor plot = plan.Plots[plotIndex];
                    // The spring plot reserves an outdoor site, not a solid
                    // building. Its walkable approach enters that site; the
                    // brook builder owns the actual bowl and ledge colliders.
                    if (plot.Kind == AlpineVillagePlotKind.Spring)
                    {
                        continue;
                    }
                    float clearance = MeasureFootprintClearance(path, plot);
                    if (clearance + GeometryTolerance >= envelope)
                    {
                        continue;
                    }

                    string relationship = string.Equals(
                        path.OwnerPlotStableId,
                        plot.StableId,
                        StringComparison.Ordinal)
                        ? "its owner"
                        : "foreign";
                    throw new InvalidOperationException(
                        $"Village path '{path.StableId}' enters {relationship} " +
                        $"plot '{plot.StableId}' by " +
                        $"{envelope - clearance:0.00} m.");
                }
            }
        }

        internal static float MeasureFootprintClearance(
            AlpineVillagePathDescriptor path,
            AlpineVillagePlotDescriptor plot)
        {
            return MeasureFootprintClearance(
                path.Start,
                path.End,
                plot);
        }

        internal static bool SegmentClearsAllFootprints(
            AlpineVillagePlan plan,
            Vector3 start,
            Vector3 end,
            float envelope)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (envelope <= 0f || float.IsNaN(envelope) ||
                float.IsInfinity(envelope))
            {
                throw new ArgumentOutOfRangeException(nameof(envelope));
            }

            for (int index = 0; index < plan.Plots.Count; index++)
            {
                if (plan.Plots[index].Kind == AlpineVillagePlotKind.Spring)
                {
                    continue;
                }
                if (MeasureFootprintClearance(
                        start,
                        end,
                        plan.Plots[index]) + GeometryTolerance < envelope)
                {
                    return false;
                }
            }

            return true;
        }

        private static float MeasureFootprintClearance(
            Vector3 pathStart,
            Vector3 pathEnd,
            AlpineVillagePlotDescriptor plot)
        {
            if (plot == null)
            {
                throw new ArgumentNullException(nameof(plot));
            }

            Vector2 forward = ToXZ(plot.Facing).normalized;
            Vector2 right = new Vector2(forward.y, -forward.x);
            Vector2 center = ToXZ(plot.GroundCenter);
            Vector2 startDelta = ToXZ(pathStart) - center;
            Vector2 endDelta = ToXZ(pathEnd) - center;
            Vector2 start = new Vector2(
                Vector2.Dot(startDelta, right),
                Vector2.Dot(startDelta, forward));
            Vector2 end = new Vector2(
                Vector2.Dot(endDelta, right),
                Vector2.Dot(endDelta, forward));
            Vector2 half = plot.FootprintSize * 0.5f;
            return SegmentToRectangleDistance(start, end, half);
        }

        private static float SegmentToRectangleDistance(
            Vector2 start,
            Vector2 end,
            Vector2 half)
        {
            if (SegmentIntersectsRectangle(start, end, half))
            {
                return 0f;
            }

            float closest = Mathf.Min(
                PointToRectangleDistance(start, half),
                PointToRectangleDistance(end, half));
            Vector2[] corners =
            {
                new Vector2(half.x, half.y),
                new Vector2(-half.x, half.y),
                new Vector2(-half.x, -half.y),
                new Vector2(half.x, -half.y)
            };
            for (int index = 0; index < corners.Length; index++)
            {
                closest = Mathf.Min(
                    closest,
                    PointToSegmentDistance(
                        corners[index],
                        start,
                        end));
            }

            return closest;
        }

        private static bool SegmentIntersectsRectangle(
            Vector2 start,
            Vector2 end,
            Vector2 half)
        {
            Vector2 delta = end - start;
            float minimum = 0f;
            float maximum = 1f;
            return ClipAxis(
                       start.x,
                       delta.x,
                       half.x,
                       ref minimum,
                       ref maximum) &&
                   ClipAxis(
                       start.y,
                       delta.y,
                       half.y,
                       ref minimum,
                       ref maximum);
        }

        private static bool ClipAxis(
            float start,
            float delta,
            float half,
            ref float minimum,
            ref float maximum)
        {
            if (Mathf.Abs(delta) <= 0.000001f)
            {
                return Mathf.Abs(start) <= half;
            }

            float first = (-half - start) / delta;
            float second = (half - start) / delta;
            if (first > second)
            {
                float swap = first;
                first = second;
                second = swap;
            }

            minimum = Mathf.Max(minimum, first);
            maximum = Mathf.Min(maximum, second);
            return minimum <= maximum;
        }

        private static float PointToRectangleDistance(
            Vector2 point,
            Vector2 half)
        {
            float x = Mathf.Max(Mathf.Abs(point.x) - half.x, 0f);
            float y = Mathf.Max(Mathf.Abs(point.y) - half.y, 0f);
            return Mathf.Sqrt(x * x + y * y);
        }

        private static float PointToSegmentDistance(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            float amount = lengthSquared <= 0.000001f
                ? 0f
                : Mathf.Clamp01(
                    Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * amount);
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }
    }

    /// <summary>
    /// Shared authored anchors for the small objects that explain village
    /// life. Rendering and audio both read this form contract; neither owns
    /// the other's positions or semantic IDs.
    /// </summary>
    public static class AlpineVillageDressingPlanner
    {
        public const string StationMechanismOwnerStableId =
            "village-station-return-bullwheel";
        public const string GarlandOwnerStableId =
            "village-garland-wire-03";
        public const string CableGateOwnerStableId =
            "village-cable-gate";
        public const string SourceBowlOwnerStableId =
            "village-chapel-source-bowl";

        public const int AudibleGarlandSpanIndex = 3;
        public const float DogHouseLaneFraction = 0.52f;
        public const float CableGateLaneInset = 0.08f;
        public const float CableGateAlongLaneOffset = 2.75f;
        public const float DogDepthBehindGate = 1.75f;

        public static Vector3 GetCableGatePosition(
            AlpineVillagePlan plan,
            AlpineVillagePlotDescriptor house)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (house == null ||
                house.Kind != AlpineVillagePlotKind.House)
            {
                throw new ArgumentException(
                    "A cable gate needs an ordinary house plot.",
                    nameof(house));
            }

            AlpineVillageLaneSample lane = plan.Lane.Sample(
                house.LaneDistance);
            Vector3 awayFromLane = -house.Facing;
            Vector3 position = lane.Position +
                awayFromLane *
                (lane.Width * 0.5f + CableGateLaneInset) +
                lane.Forward * CableGateAlongLaneOffset;
            position.y = AlpineVillageTerrainSampler.SampleHeight(
                plan,
                new Vector2(position.x, position.z));
            return position;
        }
    }
}
