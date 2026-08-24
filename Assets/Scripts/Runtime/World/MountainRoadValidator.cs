using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class MountainRoadValidator
    {
        private const float PositionTolerance = 0.03f;
        private const float GroundTolerance = 0.015f;
        private const float MinimumTreeRoadClearance = 0.65f;

        public static void ValidateOrThrow(MountainRoadPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            ValidateTunnel(plan);
            ValidateRoute(plan.Route);
            ValidatePlateau(plan);
            ValidateForest(plan);
            ValidateMiscAndSounds(plan);
            ValidateRidges(plan);
            ValidateBounds(plan);
            MountainRoadTerminalValidator.ValidateOrThrow(plan);
        }

        private static void ValidateTunnel(MountainRoadPlan plan)
        {
            MountainRoadTunnelDescriptor tunnel = plan.Tunnel;
            RequireFinite(tunnel.PortalGroundCenter, "Tunnel portal");
            RequireFinite(tunnel.SpawnPosition, "Tunnel spawn");
            RequireNormalized(tunnel.OutwardAxis, "Tunnel outward axis");
            RequireApproximately(
                tunnel.OpeningWidth,
                CityMountainBoundaryDefinition.TunnelOpeningWidth,
                "Tunnel opening width");
            RequireApproximately(
                tunnel.OpeningHeight,
                CityMountainBoundaryDefinition.TunnelOpeningHeight,
                "Tunnel opening height");
            float spawnDepth = Vector3.Dot(
                tunnel.PortalGroundCenter - tunnel.SpawnPosition,
                tunnel.OutwardAxis);
            RequireApproximately(
                spawnDepth,
                MountainRoadPlanner.SpawnDepth,
                "Spawn depth");
            Vector3 lateral = tunnel.PortalGroundCenter -
                              tunnel.SpawnPosition -
                              tunnel.OutwardAxis * spawnDepth;
            if (lateral.sqrMagnitude > PositionTolerance * PositionTolerance)
            {
                throw new InvalidOperationException(
                    "Mountain-road spawn must sit on the tunnel axis.");
            }

            if (Vector3.Dot(plan.SpawnForward, tunnel.OutwardAxis) < 0.999f)
            {
                throw new InvalidOperationException(
                    "Mountain-road spawn must face out of the tunnel.");
            }

            float terrainY = MountainRoadTerrainSampler.SampleHeight(
                plan.Route,
                plan.Plateau,
                new Vector2(
                    tunnel.SpawnPosition.x,
                    tunnel.SpawnPosition.z));
            RequireApproximately(
                terrainY,
                tunnel.SpawnPosition.y -
                MountainRoadTerrainSampler.RoadBedClearance,
                "Terrain below tunnel floor");
        }

        private static void ValidateRoute(MountainRoadRoutePlan route)
        {
            if (route == null || route.Samples.Count < 60)
            {
                throw new InvalidOperationException(
                    "Mountain road needs a densely sampled authored route.");
            }

            RequireApproximately(
                route.Length,
                MountainRoadPlanner.OutdoorRouteLength,
                "Outdoor route length");
            RequireApproximately(route.Start.x, 0f, "Route start X");
            RequireApproximately(route.Start.y, 0f, "Route start Y");
            RequireApproximately(route.Start.z, 0f, "Route start Z");
            RequireApproximately(
                route.ElevationGain,
                MountainRoadPlanner.ElevationGain,
                "Route elevation gain");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            int firstHairpinSamples = 0;
            int secondHairpinSamples = 0;
            float maximumGap = 0f;
            float previousDistance = -1f;
            float previousY = float.NegativeInfinity;
            for (int index = 0; index < route.Samples.Count; index++)
            {
                MountainRoadRouteSample sample = route.Samples[index];
                if (string.IsNullOrWhiteSpace(sample.StableId) ||
                    !ids.Add(sample.StableId))
                {
                    throw new InvalidOperationException(
                        "Route sample IDs must be non-empty and unique.");
                }

                RequireFinite(sample.Position, sample.StableId);
                RequireNormalized(sample.Forward, sample.StableId + " forward");
                if (sample.Distance <= previousDistance && index > 0)
                {
                    throw new InvalidOperationException(
                        "Route cumulative distance must increase strictly.");
                }

                if (sample.Position.y + PositionTolerance < previousY)
                {
                    throw new InvalidOperationException(
                        "The mountain road must climb monotonically.");
                }

                if (sample.Width < MountainRoadPlanner.RoadWidth - 0.01f ||
                    sample.Width > MountainRoadPlanner.HairpinWidth + 0.01f)
                {
                    throw new InvalidOperationException(
                        $"{sample.StableId} has an invalid road width.");
                }

                if (index > 0)
                {
                    maximumGap = Mathf.Max(
                        maximumGap,
                        sample.Distance - previousDistance);
                }

                if (sample.HairpinIndex == 0)
                {
                    firstHairpinSamples++;
                }
                else if (sample.HairpinIndex == 1)
                {
                    secondHairpinSamples++;
                }
                else if (sample.HairpinIndex != -1)
                {
                    throw new InvalidOperationException(
                        "Only the two authored hairpin indices are valid.");
                }

                previousDistance = sample.Distance;
                previousY = sample.Position.y;
            }

            if (maximumGap > 1.08f ||
                firstHairpinSamples < 17 ||
                secondHairpinSamples < 17)
            {
                throw new InvalidOperationException(
                    "Route sampling is too sparse for a continuous ribbon.");
            }

            ValidateHairpin(
                route,
                0,
                route.FirstHairpinStart,
                route.FirstHairpinEnd);
            ValidateHairpin(
                route,
                1,
                route.SecondHairpinStart,
                route.SecondHairpinEnd);
            ValidateNoNonAdjacentRoadOverlap(route);
        }

        private static void ValidateHairpin(
            MountainRoadRoutePlan route,
            int hairpinIndex,
            float startDistance,
            float endDistance)
        {
            RequireApproximately(
                endDistance - startDistance,
                Mathf.PI * MountainRoadPlanner.HairpinRadius,
                $"Hairpin {hairpinIndex} arc length");
            MountainRoadRouteSample start = route.Sample(startDistance);
            MountainRoadRouteSample middle = route.Sample(
                (startDistance + endDistance) * 0.5f);
            MountainRoadRouteSample end = route.Sample(endDistance);
            Vector2 center = new Vector2(
                (start.Position.x + end.Position.x) * 0.5f,
                (start.Position.z + end.Position.z) * 0.5f);
            RequireApproximately(
                Vector2.Distance(
                    new Vector2(middle.Position.x, middle.Position.z),
                    center),
                MountainRoadPlanner.HairpinRadius,
                $"Hairpin {hairpinIndex} radius");
            if (Vector3.Dot(start.Forward, end.Forward) > -0.98f)
            {
                throw new InvalidOperationException(
                    $"Hairpin {hairpinIndex} does not reverse the road.");
            }

            if (middle.Width < MountainRoadPlanner.HairpinWidth - 0.01f)
            {
                throw new InvalidOperationException(
                    $"Hairpin {hairpinIndex} is not widened at its apex.");
            }
        }

        private static void ValidateNoNonAdjacentRoadOverlap(
            MountainRoadRoutePlan route)
        {
            for (int first = 1; first < route.Samples.Count; first++)
            {
                MountainRoadRouteSample a0 = route.Samples[first - 1];
                MountainRoadRouteSample a1 = route.Samples[first];
                Vector2 aStart = ToXZ(a0.Position);
                Vector2 aEnd = ToXZ(a1.Position);
                for (int second = first + 5;
                     second < route.Samples.Count;
                     second++)
                {
                    MountainRoadRouteSample b0 = route.Samples[second - 1];
                    MountainRoadRouteSample b1 = route.Samples[second];
                    // Neighbouring pieces of the same widened ribbon remain
                    // geometrically close for several one-metre samples on
                    // one continuous arc. They are one surface, not a
                    // self-intersection; only separated route chapters are
                    // relevant here.
                    if (Mathf.Abs(a0.Distance - b0.Distance) < 8.25f)
                    {
                        continue;
                    }

                    float distance = SegmentDistance(
                        aStart,
                        aEnd,
                        ToXZ(b0.Position),
                        ToXZ(b1.Position));
                    float required = Mathf.Max(a0.Width, a1.Width) * 0.5f +
                                     Mathf.Max(b0.Width, b1.Width) * 0.5f +
                                     0.25f;
                    if (distance < required)
                    {
                        throw new InvalidOperationException(
                            "Non-adjacent road ribbons overlap and would " +
                            "allow a hairpin shortcut: " +
                            $"{a0.StableId}->{a1.StableId} and " +
                            $"{b0.StableId}->{b1.StableId}, " +
                            $"distance {distance:0.###}, required " +
                            $"{required:0.###}.");
                    }
                }
            }
        }

        private static void ValidatePlateau(MountainRoadPlan plan)
        {
            MountainRoadPlateauDescriptor plateau = plan.Plateau;
            if (plateau.VerticesXZ.Count < 8)
            {
                throw new InvalidOperationException(
                    "The endpoint plateau needs an irregular authored rim.");
            }

            if (plateau.Size.x < 41.9f || plateau.Size.x > 42.1f ||
                plateau.Size.y < 26.9f || plateau.Size.y > 27.1f)
            {
                throw new InvalidOperationException(
                    "Endpoint terminal must stay approximately 42 x 27 m.");
            }

            if (!plateau.Contains(ToXZ(plan.Route.End)))
            {
                throw new InvalidOperationException(
                    "The route endpoint must lie inside the plateau.");
            }

            RequireApproximately(
                plateau.Center.y,
                plan.Route.End.y,
                "Plateau surface height");
            RequireApproximately(
                plateau.EntryDistance,
                plan.Route.Length - 5f,
                "Plateau entry distance");

            MountainRoadRouteSample entry = plan.Route.Sample(
                plateau.EntryDistance);
            RequireApproximately(
                entry.Position.y,
                plateau.Center.y,
                "Plateau entry surface height");
            Vector2 expectedLeft = ToXZ(
                entry.Position - entry.Right * (entry.Width * 0.5f));
            Vector2 expectedRight = ToXZ(
                entry.Position + entry.Right * (entry.Width * 0.5f));
            if (Vector2.Distance(expectedLeft, plateau.VerticesXZ[0]) >
                    PositionTolerance ||
                Vector2.Distance(
                    expectedRight,
                    plateau.VerticesXZ[plateau.VerticesXZ.Count - 1]) >
                    PositionTolerance)
            {
                throw new InvalidOperationException(
                    "The road ribbon must share both entry corners with " +
                    "the terminal plateau.");
            }
        }

        private static void ValidateForest(MountainRoadPlan plan)
        {
            int physical = 0;
            int mid = 0;
            int far = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < plan.Forest.Count; index++)
            {
                MountainRoadForestDescriptor tree = plan.Forest[index];
                if (string.IsNullOrWhiteSpace(tree.StableId) ||
                    !ids.Add(tree.StableId))
                {
                    throw new InvalidOperationException(
                        "Forest IDs must be non-empty and unique.");
                }

                RequireFinite(tree.Position, tree.StableId);
                if (tree.Height < 6.9f || tree.Height > 20.6f ||
                    tree.CrownRadius < 1.25f || tree.CrownRadius > 3.85f)
                {
                    throw new InvalidOperationException(
                        $"{tree.StableId} has an invalid low-poly envelope.");
                }

                Vector2 point = ToXZ(tree.Position);
                if (!plan.TerrainBoundsXZ.Contains(point) ||
                    plan.Plateau.Contains(point) ||
                    plan.Terminal.Cableway.ContainsClearanceXZ(
                        point,
                        tree.CrownRadius + 0.8f))
                {
                    throw new InvalidOperationException(
                        $"{tree.StableId} is outside grounded forest terrain.");
                }

                float expectedY = MountainRoadTerrainSampler.SampleHeight(
                    plan.Route,
                    plan.Plateau,
                    point);
                if (Mathf.Abs(expectedY - tree.Position.y) > GroundTolerance)
                {
                    throw new InvalidOperationException(
                        $"{tree.StableId} is not grounded on the terrain plan.");
                }

                MountainRoadTerrainSampler.FindClosest(
                    plan.Route,
                    point,
                    out float distance,
                    out _,
                    out _,
                    out float halfWidth);
                if (distance < halfWidth + tree.CrownRadius +
                    MinimumTreeRoadClearance)
                {
                    throw new InvalidOperationException(
                        $"{tree.StableId} enters the road clearance.");
                }

                switch (tree.Layer)
                {
                    case MountainRoadForestLayer.Physical:
                        physical++;
                        if (!tree.BlocksMovement)
                        {
                            throw new InvalidOperationException(
                                "Every physical tree needs a trunk collider.");
                        }
                        break;
                    case MountainRoadForestLayer.Mid:
                        mid++;
                        if (tree.BlocksMovement)
                        {
                            throw new InvalidOperationException(
                                "Mid forest must not spend collider budget.");
                        }
                        break;
                    case MountainRoadForestLayer.Far:
                        far++;
                        if (tree.BlocksMovement)
                        {
                            throw new InvalidOperationException(
                                "Far forest must not spend collider budget.");
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            if (physical < 40 || physical > 50 ||
                mid < 70 || mid > 100 ||
                far < 90 || far > 140)
            {
                throw new InvalidOperationException(
                    "Forest layer budgets drifted outside the authored range.");
            }
        }

        private static void ValidateMiscAndSounds(MountainRoadPlan plan)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new HashSet<MountainRoadMiscKind>();
            var byId = new Dictionary<string, MountainRoadMiscDescriptor>(
                StringComparer.Ordinal);
            for (int index = 0; index < plan.Misc.Count; index++)
            {
                MountainRoadMiscDescriptor item = plan.Misc[index];
                if (string.IsNullOrWhiteSpace(item.StableId) ||
                    !ids.Add(item.StableId))
                {
                    throw new InvalidOperationException(
                        "Misc IDs must be non-empty and unique.");
                }

                RequireFinite(item.Position, item.StableId);
                RequirePositive(item.Size, item.StableId + " size");
                byId.Add(item.StableId, item);
                kinds.Add(item.Kind);
            }

            MountainRoadMiscKind[] semanticObjects =
            {
                MountainRoadMiscKind.Culvert,
                MountainRoadMiscKind.GuardRail,
                MountainRoadMiscKind.ConvexMirror,
                MountainRoadMiscKind.UtilityCabinet,
                MountainRoadMiscKind.UtilityCable,
                MountainRoadMiscKind.SnowPole,
                MountainRoadMiscKind.TunnelLamp
            };
            for (int index = 0; index < semanticObjects.Length; index++)
            {
                if (!kinds.Contains(semanticObjects[index]))
                {
                    throw new InvalidOperationException(
                        $"Missing semantic mountain-road object " +
                        $"{semanticObjects[index]}.");
                }
            }

            var soundIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < plan.SoundAnchors.Count; index++)
            {
                MountainRoadSoundAnchor sound = plan.SoundAnchors[index];
                if (string.IsNullOrWhiteSpace(sound.StableId) ||
                    !soundIds.Add(sound.StableId) ||
                    !byId.TryGetValue(sound.SourceObjectStableId, out var source))
                {
                    throw new InvalidOperationException(
                        "Every sound anchor needs a unique ID and visible source.");
                }

                if (Vector3.Distance(sound.Position, source.Position) > 0.01f ||
                    sound.AudibleRadius < 4f ||
                    sound.AudibleRadius > 10f)
                {
                    throw new InvalidOperationException(
                        $"{sound.StableId} is detached from its visible source.");
                }
            }

            if (plan.SoundAnchors.Count != 5)
            {
                throw new InvalidOperationException(
                    "The authored area exposes exactly five causal sound anchors.");
            }
        }

        private static void ValidateRidges(MountainRoadPlan plan)
        {
            int mid = 0;
            int snowy = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < plan.Ridges.Count; index++)
            {
                MountainRoadRidgeDescriptor ridge = plan.Ridges[index];
                if (string.IsNullOrWhiteSpace(ridge.StableId) ||
                    !ids.Add(ridge.StableId))
                {
                    throw new InvalidOperationException(
                        "Ridge IDs must be non-empty and unique.");
                }

                RequireFinite(ridge.Center, ridge.StableId);
                RequirePositive(ridge.Size, ridge.StableId + " size");
                if (ridge.Layer == MountainRoadRidgeLayer.Mid)
                {
                    mid++;
                }
                else if (ridge.Layer == MountainRoadRidgeLayer.FarSnow)
                {
                    snowy++;
                }
                else
                {
                    throw new ArgumentOutOfRangeException();
                }
            }

            if (mid < 6 || snowy < 10)
            {
                throw new InvalidOperationException(
                    "The mountain amphitheatre lacks a depth layer.");
            }
        }

        private static void ValidateBounds(MountainRoadPlan plan)
        {
            if (plan.TerrainBoundsXZ.width < 50f ||
                plan.TerrainBoundsXZ.height < 50f ||
                !Contains(plan.WorldBounds, plan.SpawnPosition) ||
                !Contains(plan.WorldBounds, plan.Route.End))
            {
                throw new InvalidOperationException(
                    "Mountain-road world bounds do not contain the playable area.");
            }

            for (int index = 0;
                 index < plan.Terminal.Cafe.FootprintXZ.Count;
                 index++)
            {
                Vector2 footprint =
                    plan.Terminal.Cafe.FootprintXZ[index];
                if (!Contains(
                        plan.WorldBounds,
                        new Vector3(
                            footprint.x,
                            plan.Terminal.Cafe.FloorY +
                            plan.Terminal.Cafe.Height,
                            footprint.y)))
                {
                    throw new InvalidOperationException(
                        "Mountain-road bounds omit the terminal cafe.");
                }
            }

            for (int index = 0;
                 index < plan.Terminal.Cableway.Nodes.Count;
                 index++)
            {
                MountainCablewayNodeDescriptor node =
                    plan.Terminal.Cableway.Nodes[index];
                if (!Contains(plan.WorldBounds, node.CableCenter) ||
                    !Contains(plan.WorldBounds, node.GroundPosition))
                {
                    throw new InvalidOperationException(
                        "Mountain-road bounds omit the cableway.");
                }
            }

            for (int index = 0; index < plan.Ridges.Count; index++)
            {
                if (!ContainsRidgeEnvelope(
                        plan.WorldBounds,
                        plan.Ridges[index]))
                {
                    throw new InvalidOperationException(
                        "Mountain-road bounds omit a mountain ridge.");
                }
            }
        }

        private static bool ContainsRidgeEnvelope(
            Bounds bounds,
            MountainRoadRidgeDescriptor ridge)
        {
            Vector3 halfSize = ridge.Size * 0.5f;
            float yaw = ridge.YawDegrees * Mathf.Deg2Rad;
            float halfX = Mathf.Abs(Mathf.Cos(yaw)) * halfSize.x +
                          Mathf.Abs(Mathf.Sin(yaw)) * halfSize.z;
            float halfZ = Mathf.Abs(Mathf.Sin(yaw)) * halfSize.x +
                          Mathf.Abs(Mathf.Cos(yaw)) * halfSize.z;
            return Contains(
                       bounds,
                       ridge.Center + new Vector3(
                           halfX,
                           halfSize.y,
                           halfZ)) &&
                   Contains(
                       bounds,
                       ridge.Center - new Vector3(
                           halfX,
                           halfSize.y,
                           halfZ));
        }

        private static float SegmentDistance(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            if (SegmentsIntersect(a, b, c, d))
            {
                return 0f;
            }

            return Mathf.Min(
                PointSegmentDistance(a, c, d),
                PointSegmentDistance(b, c, d),
                PointSegmentDistance(c, a, b),
                PointSegmentDistance(d, a, b));
        }

        private static bool SegmentsIntersect(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            const float epsilon = 0.00001f;
            float abC = Cross(b - a, c - a);
            float abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c);
            float cdB = Cross(d - c, b - c);
            bool proper = ((abC > epsilon && abD < -epsilon) ||
                           (abC < -epsilon && abD > epsilon)) &&
                          ((cdA > epsilon && cdB < -epsilon) ||
                           (cdA < -epsilon && cdB > epsilon));
            if (proper)
            {
                return true;
            }

            return (Mathf.Abs(abC) <= epsilon && OnSegment(a, b, c)) ||
                   (Mathf.Abs(abD) <= epsilon && OnSegment(a, b, d)) ||
                   (Mathf.Abs(cdA) <= epsilon && OnSegment(c, d, a)) ||
                   (Mathf.Abs(cdB) <= epsilon && OnSegment(c, d, b));
        }

        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 point)
        {
            const float epsilon = 0.00001f;
            return point.x >= Mathf.Min(a.x, b.x) - epsilon &&
                   point.x <= Mathf.Max(a.x, b.x) + epsilon &&
                   point.y >= Mathf.Min(a.y, b.y) - epsilon &&
                   point.y <= Mathf.Max(a.y, b.y) + epsilon;
        }

        private static float PointSegmentDistance(
            Vector2 point,
            Vector2 a,
            Vector2 b)
        {
            Vector2 ab = b - a;
            float denominator = ab.sqrMagnitude;
            float t = denominator <= 0.000001f
                ? 0f
                : Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
            return Vector2.Distance(point, Vector2.Lerp(a, b, t));
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        private static bool Contains(Bounds bounds, Vector3 point)
        {
            const float tolerance = 0.01f;
            return point.x >= bounds.min.x - tolerance &&
                   point.x <= bounds.max.x + tolerance &&
                   point.y >= bounds.min.y - tolerance &&
                   point.y <= bounds.max.y + tolerance &&
                   point.z >= bounds.min.z - tolerance &&
                   point.z <= bounds.max.z + tolerance;
        }

        private static void RequireFinite(Vector3 value, string name)
        {
            if (float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                float.IsNaN(value.z) || float.IsInfinity(value.z))
            {
                throw new InvalidOperationException($"{name} must be finite.");
            }
        }

        private static void RequirePositive(Vector3 value, string name)
        {
            RequireFinite(value, name);
            if (value.x <= 0f || value.y <= 0f || value.z <= 0f)
            {
                throw new InvalidOperationException($"{name} must be positive.");
            }
        }

        private static void RequireNormalized(Vector3 value, string name)
        {
            RequireFinite(value, name);
            if (Mathf.Abs(value.magnitude - 1f) > 0.01f)
            {
                throw new InvalidOperationException($"{name} must be normalized.");
            }
        }

        private static void RequireApproximately(
            float actual,
            float expected,
            string name)
        {
            if (Mathf.Abs(actual - expected) > PositionTolerance)
            {
                throw new InvalidOperationException(
                    $"{name} expected {expected:0.###}, got {actual:0.###}.");
            }
        }
    }
}
