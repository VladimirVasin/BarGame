using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class MountainRoadPlanner
    {
        public const int DefaultSeed = 19081987;
        public const float OutdoorRouteLength = 82.7f;
        public const float ElevationGain = 8.7f;
        public const float HairpinRadius = 7.5f;
        public const float RoadWidth = 4.8f;
        public const float HairpinWidth = 6.4f;
        public const float SpawnDepth = 6f;
        public const float TunnelVisualDepth = 9f;
        public const float TerrainMargin = 30f;

        private const float LowerRunLength = 16f;
        private const float MiddleShelfEnd = 45f;
        private const float FirstHairpinEndY = 3.2f;
        private const float MiddleShelfEndY = 4.4f;
        private const float SecondHairpinEndY = 7.2f;
        private const float PlateauEntryLead = 5f;
        private const float ForestRoadClearance = 0.75f;
        private const float RoadsidePropClearance = 0.8f;

        private sealed class MutablePoint
        {
            internal MutablePoint(
                float distance,
                Vector3 position,
                MountainRoadRouteSection section,
                int hairpinIndex)
            {
                Distance = distance;
                Position = position;
                Section = section;
                HairpinIndex = hairpinIndex;
            }

            internal float Distance { get; }
            internal Vector3 Position { get; }
            internal MountainRoadRouteSection Section { get; }
            internal int HairpinIndex { get; }
        }

        public static MountainRoadPlan Create(int seed = DefaultSeed)
        {
            MountainRoadRoutePlan route = CreateRoute();
            MountainRoadTunnelDescriptor tunnel = CreateTunnel();
            MountainRoadPlateauDescriptor plateau = CreatePlateau(route);
            MountainRoadTerminalPlan terminal =
                MountainRoadTerminalPlanner.Create(route, plateau);
            Rect terrainBounds = CalculateTerrainBounds(
                route,
                plateau,
                terminal);
            List<MountainRoadForestDescriptor> forest =
                CreateForest(
                    seed,
                    route,
                    plateau,
                    terminal,
                    terrainBounds);
            List<MountainRoadMiscDescriptor> misc =
                CreateMisc(seed, tunnel, route, plateau);
            List<MountainRoadSoundAnchor> sounds =
                CreateSoundAnchors(misc);
            List<MountainRoadRidgeDescriptor> ridges =
                CreateRidges(
                    seed,
                    route,
                    plateau,
                    terminal);
            Bounds worldBounds = CalculateWorldBounds(
                terrainBounds,
                terminal,
                ridges);
            var plan = new MountainRoadPlan(
                seed,
                tunnel,
                route,
                plateau,
                terminal,
                terrainBounds,
                worldBounds,
                forest,
                misc,
                ridges,
                sounds);
            MountainRoadValidator.ValidateOrThrow(plan);
            return plan;
        }

        private static Bounds CalculateWorldBounds(
            Rect terrainBounds,
            MountainRoadTerminalPlan terminal,
            IReadOnlyList<MountainRoadRidgeDescriptor> ridges)
        {
            float minimumX = terrainBounds.xMin;
            float maximumX = terrainBounds.xMax;
            float minimumY = -12f;
            float maximumY = terminal.Cableway.UpperCableCenter.y + 6f;
            float minimumZ = terrainBounds.yMin;
            float maximumZ = terrainBounds.yMax;
            for (int index = 0; index < ridges.Count; index++)
            {
                MountainRoadRidgeDescriptor ridge = ridges[index];
                Vector3 halfSize = ridge.Size * 0.5f;
                float yaw = ridge.YawDegrees * Mathf.Deg2Rad;
                float halfX = Mathf.Abs(Mathf.Cos(yaw)) * halfSize.x +
                              Mathf.Abs(Mathf.Sin(yaw)) * halfSize.z;
                float halfZ = Mathf.Abs(Mathf.Sin(yaw)) * halfSize.x +
                              Mathf.Abs(Mathf.Cos(yaw)) * halfSize.z;
                minimumX = Mathf.Min(minimumX, ridge.Center.x - halfX);
                maximumX = Mathf.Max(maximumX, ridge.Center.x + halfX);
                minimumY = Mathf.Min(minimumY, ridge.Center.y - halfSize.y);
                maximumY = Mathf.Max(maximumY, ridge.Center.y + halfSize.y);
                minimumZ = Mathf.Min(minimumZ, ridge.Center.z - halfZ);
                maximumZ = Mathf.Max(maximumZ, ridge.Center.z + halfZ);
            }

            var minimum = new Vector3(minimumX, minimumY, minimumZ);
            var maximum = new Vector3(maximumX, maximumY, maximumZ);
            return new Bounds(
                (minimum + maximum) * 0.5f,
                maximum - minimum);
        }

        private static MountainRoadTunnelDescriptor CreateTunnel()
        {
            Vector3 axis = Vector3.forward;
            Vector3 portal = Vector3.zero;
            return new MountainRoadTunnelDescriptor(
                portal,
                axis,
                CityMountainBoundaryDefinition.TunnelOpeningWidth,
                CityMountainBoundaryDefinition.TunnelOpeningHeight,
                TunnelVisualDepth,
                portal - axis * SpawnDepth);
        }

        private static MountainRoadRoutePlan CreateRoute()
        {
            float arcLength = Mathf.PI * HairpinRadius;
            float firstArcStart = LowerRunLength;
            float firstArcEnd = firstArcStart + arcLength;
            float secondArcStart = MiddleShelfEnd;
            float secondArcEnd = secondArcStart + arcLength;
            var points = new List<MutablePoint>(90)
            {
                new MutablePoint(
                    0f,
                    Vector3.zero,
                    MountainRoadRouteSection.LowerClimb,
                    -1)
            };

            AppendLine(
                points,
                new Vector3(0f, 1.8f, LowerRunLength),
                LowerRunLength,
                16,
                MountainRoadRouteSection.LowerClimb);
            AppendArc(
                points,
                new Vector2(HairpinRadius, LowerRunLength),
                Mathf.PI,
                0f,
                FirstHairpinEndY,
                firstArcEnd,
                24,
                MountainRoadRouteSection.FirstHairpin,
                0);
            AppendLine(
                points,
                new Vector3(
                    HairpinRadius * 2f,
                    MiddleShelfEndY,
                    LowerRunLength - (MiddleShelfEnd - firstArcEnd)),
                MiddleShelfEnd,
                15,
                MountainRoadRouteSection.MiddleShelf);
            Vector3 secondStart = points[points.Count - 1].Position;
            AppendArc(
                points,
                new Vector2(
                    secondStart.x + HairpinRadius,
                    secondStart.z),
                Mathf.PI,
                Mathf.PI * 2f,
                SecondHairpinEndY,
                secondArcEnd,
                24,
                MountainRoadRouteSection.SecondHairpin,
                1);
            Vector3 secondEnd = points[points.Count - 1].Position;
            AppendLine(
                points,
                new Vector3(
                    secondEnd.x,
                    ElevationGain,
                    secondEnd.z +
                    OutdoorRouteLength - PlateauEntryLead - secondArcEnd),
                OutdoorRouteLength - PlateauEntryLead,
                10,
                MountainRoadRouteSection.UpperApproach);
            Vector3 plateauEntry = points[points.Count - 1].Position;
            AppendLine(
                points,
                new Vector3(
                    plateauEntry.x,
                    ElevationGain,
                    plateauEntry.z + PlateauEntryLead),
                OutdoorRouteLength,
                5,
                MountainRoadRouteSection.UpperApproach);

            var samples = new List<MountainRoadRouteSample>(points.Count);
            for (int index = 0; index < points.Count; index++)
            {
                MutablePoint point = points[index];
                Vector3 forward;
                if (index == 0)
                {
                    forward = points[1].Position - point.Position;
                }
                else if (index == points.Count - 1)
                {
                    forward = point.Position - points[index - 1].Position;
                }
                else
                {
                    forward = points[index + 1].Position -
                              points[index - 1].Position;
                }

                forward.y = 0f;
                forward.Normalize();
                samples.Add(new MountainRoadRouteSample(
                    $"mountain-route-{index:000}",
                    point.Distance,
                    point.Position,
                    forward,
                    EvaluateWidth(
                        point.Distance,
                        firstArcStart,
                        firstArcEnd,
                        secondArcStart,
                        secondArcEnd),
                    point.Section,
                    point.HairpinIndex));
            }

            return new MountainRoadRoutePlan(
                samples,
                OutdoorRouteLength,
                firstArcStart,
                firstArcEnd,
                secondArcStart,
                secondArcEnd);
        }

        private static void AppendLine(
            ICollection<MutablePoint> target,
            Vector3 end,
            float endDistance,
            int divisions,
            MountainRoadRouteSection section)
        {
            var list = (List<MutablePoint>)target;
            MutablePoint start = list[list.Count - 1];
            for (int step = 1; step <= divisions; step++)
            {
                float t = step / (float)divisions;
                target.Add(new MutablePoint(
                    Mathf.Lerp(start.Distance, endDistance, t),
                    Vector3.Lerp(start.Position, end, t),
                    section,
                    -1));
            }
        }

        private static void AppendArc(
            ICollection<MutablePoint> target,
            Vector2 centerXZ,
            float startAngle,
            float endAngle,
            float endY,
            float endDistance,
            int divisions,
            MountainRoadRouteSection section,
            int hairpinIndex)
        {
            var list = (List<MutablePoint>)target;
            MutablePoint start = list[list.Count - 1];
            for (int step = 1; step <= divisions; step++)
            {
                float t = step / (float)divisions;
                float angle = Mathf.Lerp(startAngle, endAngle, t);
                target.Add(new MutablePoint(
                    Mathf.Lerp(start.Distance, endDistance, t),
                    new Vector3(
                        centerXZ.x + Mathf.Cos(angle) * HairpinRadius,
                        Mathf.Lerp(start.Position.y, endY, t),
                        centerXZ.y + Mathf.Sin(angle) * HairpinRadius),
                    section,
                    hairpinIndex));
            }
        }

        private static float EvaluateWidth(
            float distance,
            float firstStart,
            float firstEnd,
            float secondStart,
            float secondEnd)
        {
            float first = HairpinWeight(distance, firstStart, firstEnd);
            float second = HairpinWeight(distance, secondStart, secondEnd);
            return Mathf.Lerp(
                RoadWidth,
                HairpinWidth,
                Mathf.Max(first, second));
        }

        private static float HairpinWeight(
            float distance,
            float start,
            float end)
        {
            const float shoulder = 3f;
            float enter = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(start - shoulder, start, distance));
            float exit = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(end, end + shoulder, distance));
            return Mathf.Min(enter, exit);
        }

        private static MountainRoadPlateauDescriptor CreatePlateau(
            MountainRoadRoutePlan route)
        {
            Vector3 forward = route.Samples[route.Samples.Count - 1].Forward;
            Vector3 center = route.End + forward * 4f;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector2[] local =
            {
                new Vector2(-RoadWidth * 0.5f, -9f),
                new Vector2(-8f, -8.4f),
                new Vector2(-15f, -6.5f),
                new Vector2(-20f, -2f),
                new Vector2(-21f, 7f),
                new Vector2(-20f, 13f),
                new Vector2(-17f, 17f),
                new Vector2(-8f, 18f),
                new Vector2(5f, 18f),
                new Vector2(14f, 18f),
                new Vector2(20f, 16f),
                new Vector2(21f, 8f),
                new Vector2(20f, -2f),
                new Vector2(15f, -6.5f),
                new Vector2(8f, -8.4f),
                new Vector2(RoadWidth * 0.5f, -9f)
            };
            var vertices = new List<Vector2>(local.Length);
            for (int index = 0; index < local.Length; index++)
            {
                Vector3 world = center +
                    right * local[index].x +
                    forward * local[index].y;
                vertices.Add(new Vector2(world.x, world.z));
            }

            return new MountainRoadPlateauDescriptor(
                center,
                forward,
                route.Length - PlateauEntryLead,
                vertices);
        }

        private static Rect CalculateTerrainBounds(
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau,
            MountainRoadTerminalPlan terminal)
        {
            float xMin = plateau.BoundsXZ.xMin;
            float xMax = plateau.BoundsXZ.xMax;
            float zMin = Mathf.Min(plateau.BoundsXZ.yMin, -9f);
            float zMax = plateau.BoundsXZ.yMax;
            for (int index = 0; index < route.Samples.Count; index++)
            {
                Vector3 point = route.Samples[index].Position;
                xMin = Mathf.Min(xMin, point.x);
                xMax = Mathf.Max(xMax, point.x);
                zMin = Mathf.Min(zMin, point.z);
                zMax = Mathf.Max(zMax, point.z);
            }

            Vector3 cableEnd = terminal.Cableway.UpperCableCenter;
            xMin = Mathf.Min(xMin, cableEnd.x - 12f);
            xMax = Mathf.Max(xMax, cableEnd.x + 12f);
            zMin = Mathf.Min(zMin, cableEnd.z - 12f);
            zMax = Mathf.Max(zMax, cableEnd.z + 12f);

            return Rect.MinMaxRect(
                xMin - TerrainMargin,
                zMin - TerrainMargin,
                xMax + TerrainMargin,
                zMax + TerrainMargin);
        }

        private static List<MountainRoadForestDescriptor> CreateForest(
            int seed,
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau,
            MountainRoadTerminalPlan terminal,
            Rect terrainBounds)
        {
            var result = new List<MountainRoadForestDescriptor>(242);
            AppendForestLayer(
                result,
                seed,
                route,
                plateau,
                terminal,
                terrainBounds,
                MountainRoadForestLayer.Physical,
                46,
                6.2f,
                14f,
                3.4f);
            AppendForestLayer(
                result,
                seed + 101,
                route,
                plateau,
                terminal,
                terrainBounds,
                MountainRoadForestLayer.Mid,
                84,
                11f,
                21f,
                2.5f);
            AppendForestLayer(
                result,
                seed + 307,
                route,
                plateau,
                terminal,
                terrainBounds,
                MountainRoadForestLayer.Far,
                112,
                17f,
                28f,
                1.9f);
            return result;
        }

        private static void AppendForestLayer(
            ICollection<MountainRoadForestDescriptor> target,
            int seed,
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau,
            MountainRoadTerminalPlan terminal,
            Rect terrainBounds,
            MountainRoadForestLayer layer,
            int count,
            float minimumOffset,
            float maximumOffset,
            float spacing)
        {
            var accepted = new List<Vector2>(count);
            int attempts = count * 100;
            for (int attempt = 0;
                 attempt < attempts && accepted.Count < count;
                 attempt++)
            {
                float distance = Unit(seed, attempt, 0x44495354u) *
                                 route.Length;
                MountainRoadRouteSample sample = route.Sample(distance);
                float side = (Hash(seed, attempt, 0x53494445u) & 1u) == 0u
                    ? -1f
                    : 1f;
                float lateral = Mathf.Lerp(
                    minimumOffset,
                    maximumOffset,
                    Unit(seed, attempt, 0x4F464653u));
                float along = (Unit(seed, attempt, 0x414C4F4Eu) - 0.5f) *
                              2.2f;
                Vector3 world = sample.Position +
                    sample.Right * (side * lateral) +
                    sample.Forward * along;
                Vector2 point = new Vector2(world.x, world.z);
                if (!terrainBounds.Contains(point) ||
                    plateau.BoundsXZ.Contains(point) ||
                    terminal.Cableway.ContainsClearanceXZ(point, 5f))
                {
                    continue;
                }

                MountainRoadTerrainSampler.FindClosest(
                    route,
                    point,
                    out float roadDistance,
                    out _,
                    out _,
                    out float halfWidth);
                DescribeForestEnvelope(
                    layer,
                    Unit(seed, attempt, 0x48454947u),
                    Unit(seed, attempt, 0x52414449u),
                    out float height,
                    out float radius);
                if (roadDistance <
                        halfWidth + radius + ForestRoadClearance ||
                    !HasSpacing(accepted, point, spacing))
                {
                    continue;
                }

                accepted.Add(point);
                world.y = MountainRoadTerrainSampler.SampleHeight(
                    route,
                    plateau,
                    point);
                target.Add(new MountainRoadForestDescriptor(
                    $"forest-{layer.ToString().ToLowerInvariant()}-" +
                    $"{accepted.Count - 1:000}",
                    layer,
                    world,
                    height,
                    radius,
                    Unit(seed, attempt, 0x59415721u) * 360f,
                    (int)(Hash(seed, attempt, 0x50414C45u) % 3u),
                    layer == MountainRoadForestLayer.Physical));
            }

            if (accepted.Count != count)
            {
                throw new InvalidOperationException(
                    $"Could place only {accepted.Count}/{count} {layer} " +
                    "forest anchors without entering the road corridor.");
            }
        }

        private static void DescribeForestEnvelope(
            MountainRoadForestLayer layer,
            float heightT,
            float radiusT,
            out float height,
            out float radius)
        {
            switch (layer)
            {
                case MountainRoadForestLayer.Physical:
                    height = Mathf.Lerp(7f, 12.5f, heightT);
                    radius = Mathf.Lerp(1.3f, 2.3f, radiusT);
                    return;
                case MountainRoadForestLayer.Mid:
                    height = Mathf.Lerp(8.5f, 15f, heightT);
                    radius = Mathf.Lerp(1.55f, 2.8f, radiusT);
                    return;
                case MountainRoadForestLayer.Far:
                    height = Mathf.Lerp(11f, 20.5f, heightT);
                    radius = Mathf.Lerp(2.1f, 3.8f, radiusT);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(layer));
            }
        }

        private static bool HasSpacing(
            IReadOnlyList<Vector2> accepted,
            Vector2 point,
            float spacing)
        {
            float sqr = spacing * spacing;
            for (int index = 0; index < accepted.Count; index++)
            {
                if ((accepted[index] - point).sqrMagnitude < sqr)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<MountainRoadMiscDescriptor> CreateMisc(
            int seed,
            MountainRoadTunnelDescriptor tunnel,
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau)
        {
            var result = new List<MountainRoadMiscDescriptor>(72);
            for (int index = 0; index < 22; index++)
            {
                float distance = 3f + Unit(seed, index, 0x424F554Cu) * 65f;
                float side = (index & 1) == 0 ? -1f : 1f;
                float lateral = 4.2f + Unit(seed, index, 0x4C415445u) * 4f;
                Vector3 size = new Vector3(
                    1.2f + Unit(seed, index, 0x42585A21u) * 1.8f,
                    0.8f + Unit(seed, index, 0x42592121u) * 1.3f,
                    1.2f + Unit(seed, index, 0x42585A22u) * 1.8f);
                result.Add(PlaceMisc(
                    $"misc-boulder-{index:00}",
                    MountainRoadMiscKind.Boulder,
                    route,
                    plateau,
                    distance,
                    side,
                    lateral,
                    size,
                    true,
                    Unit(seed, index, 0x42594157u) * 360f));
            }

            for (int index = 0; index < 9; index++)
            {
                result.Add(PlaceMisc(
                    $"misc-fallen-log-{index:00}",
                    MountainRoadMiscKind.FallenLog,
                    route,
                    plateau,
                    7f + index * 7.7f,
                    (index & 1) == 0 ? -1f : 1f,
                    6.2f + (index % 3) * 0.8f,
                    new Vector3(0.68f, 0.68f, 4.6f + (index % 2) * 0.9f),
                    true,
                    index * 37f + 18f));
            }

            for (int index = 0; index < 11; index++)
            {
                result.Add(PlaceMisc(
                    $"misc-stump-{index:00}",
                    MountainRoadMiscKind.Stump,
                    route,
                    plateau,
                    4f + index * 6.2f,
                    (index & 1) == 0 ? 1f : -1f,
                    5.4f + (index % 4) * 0.7f,
                    new Vector3(0.88f, 1.05f, 0.88f),
                    true,
                    index * 29f));
            }

            for (int index = 0; index < 6; index++)
            {
                result.Add(PlaceMisc(
                    $"misc-dead-tree-{index:00}",
                    MountainRoadMiscKind.DeadTree,
                    route,
                    plateau,
                    21f + index * 9.4f,
                    (index & 1) == 0 ? -1f : 1f,
                    7.8f,
                    new Vector3(0.72f, 8.2f + index * 0.45f, 0.72f),
                    true,
                    index * 51f));
            }

            AddAuthoredRoadsideObjects(result, tunnel, route, plateau);
            return result;
        }

        private static void AddAuthoredRoadsideObjects(
            ICollection<MountainRoadMiscDescriptor> result,
            MountainRoadTunnelDescriptor tunnel,
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau)
        {
            result.Add(new MountainRoadMiscDescriptor(
                "misc-tunnel-lamp",
                MountainRoadMiscKind.TunnelLamp,
                tunnel.PortalGroundCenter - tunnel.OutwardAxis * 1.8f +
                Vector3.up * 5.05f,
                Quaternion.identity,
                new Vector3(0.42f, 0.22f, 0.9f),
                false));
            result.Add(PlaceMisc(
                "misc-culvert",
                MountainRoadMiscKind.Culvert,
                route,
                plateau,
                11.5f,
                -1f,
                4.2f,
                new Vector3(2.2f, 1.3f, 2.6f),
                false,
                90f));
            result.Add(PlaceMisc(
                "misc-hairpin-mirror",
                MountainRoadMiscKind.ConvexMirror,
                route,
                plateau,
                route.FirstHairpinStart + 1.3f,
                -1f,
                4.8f,
                new Vector3(1.05f, 3f, 0.28f),
                true,
                18f));
            result.Add(PlaceMisc(
                "misc-utility-cabinet",
                MountainRoadMiscKind.UtilityCabinet,
                route,
                plateau,
                42f,
                1f,
                4.2f,
                new Vector3(1.2f, 1.8f, 0.75f),
                true,
                6f));
            result.Add(PlaceMisc(
                "misc-utility-cable",
                MountainRoadMiscKind.UtilityCable,
                route,
                plateau,
                44f,
                1f,
                5.4f,
                new Vector3(8.5f, 7.2f, 0.16f),
                false,
                4f));
            result.Add(PlaceMisc(
                "misc-abandoned-chair",
                MountainRoadMiscKind.AbandonedChair,
                route,
                plateau,
                45.5f,
                -1f,
                4.1f,
                new Vector3(0.82f, 1.1f, 0.82f),
                true,
                172f));

            float[] guardDistances = { 20f, 24.2f, 53f, 57.2f };
            for (int index = 0; index < guardDistances.Length; index++)
            {
                result.Add(PlaceMisc(
                    $"misc-guardrail-{index}",
                    MountainRoadMiscKind.GuardRail,
                    route,
                    plateau,
                    guardDistances[index],
                    -1f,
                    4.2f,
                    new Vector3(0.22f, 1.05f, 6.4f),
                    true,
                    0f));
            }

            for (int index = 0; index < 8; index++)
            {
                result.Add(PlaceMisc(
                    $"misc-snow-pole-{index}",
                    MountainRoadMiscKind.SnowPole,
                    route,
                    plateau,
                    58f + index * 1.6f,
                    (index & 1) == 0 ? -1f : 1f,
                    3.5f,
                    new Vector3(0.14f, 3f, 0.14f),
                    false,
                    0f));
            }
        }

        private static MountainRoadMiscDescriptor PlaceMisc(
            string stableId,
            MountainRoadMiscKind kind,
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau,
            float distance,
            float side,
            float lateral,
            Vector3 size,
            bool blocksMovement,
            float yawOffset)
        {
            MountainRoadRouteSample sample = route.Sample(distance);
            float yawRadians = yawOffset * Mathf.Deg2Rad;
            float crossRoadHalfExtent =
                Mathf.Abs(Mathf.Cos(yawRadians)) * size.x * 0.5f +
                Mathf.Abs(Mathf.Sin(yawRadians)) * size.z * 0.5f;
            float minimumLateral = sample.Width * 0.5f +
                                   crossRoadHalfExtent +
                                   RoadsidePropClearance;
            float resolvedLateral = Mathf.Max(lateral, minimumLateral);
            Vector3 position = sample.Position +
                               sample.Right * (side * resolvedLateral);
            Vector2 xz = new Vector2(position.x, position.z);
            position.y = MountainRoadTerrainSampler.SampleHeight(
                             route,
                             plateau,
                             xz) +
                         size.y * 0.5f;
            Quaternion rotation = Quaternion.LookRotation(
                sample.Forward,
                Vector3.up) * Quaternion.Euler(0f, yawOffset, 0f);
            return new MountainRoadMiscDescriptor(
                stableId,
                kind,
                position,
                rotation,
                size,
                blocksMovement);
        }

        private static List<MountainRoadSoundAnchor> CreateSoundAnchors(
            IReadOnlyList<MountainRoadMiscDescriptor> misc)
        {
            var byId = new Dictionary<string, MountainRoadMiscDescriptor>(
                StringComparer.Ordinal);
            for (int index = 0; index < misc.Count; index++)
            {
                byId.Add(misc[index].StableId, misc[index]);
            }

            return new List<MountainRoadSoundAnchor>
            {
                CreateSound(byId, "sound-tunnel-ballast",
                    MountainRoadSoundAnchorKind.TunnelLampBallast,
                    "misc-tunnel-lamp", 6.5f),
                CreateSound(byId, "sound-culvert-water",
                    MountainRoadSoundAnchorKind.CulvertWater,
                    "misc-culvert", 9f),
                CreateSound(byId, "sound-loose-guardrail",
                    MountainRoadSoundAnchorKind.LooseGuardRail,
                    "misc-guardrail-1", 7f),
                CreateSound(byId, "sound-utility-cable",
                    MountainRoadSoundAnchorKind.UtilityCable,
                    "misc-utility-cable", 8f),
                CreateSound(byId, "sound-snow-pole",
                    MountainRoadSoundAnchorKind.SnowPole,
                    "misc-snow-pole-6", 6f)
            };
        }

        private static MountainRoadSoundAnchor CreateSound(
            IReadOnlyDictionary<string, MountainRoadMiscDescriptor> misc,
            string stableId,
            MountainRoadSoundAnchorKind kind,
            string sourceId,
            float radius)
        {
            MountainRoadMiscDescriptor source = misc[sourceId];
            return new MountainRoadSoundAnchor(
                stableId,
                kind,
                sourceId,
                source.Position,
                radius);
        }

        private static List<MountainRoadRidgeDescriptor> CreateRidges(
            int seed,
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau,
            MountainRoadTerminalPlan terminal)
        {
            var result = new List<MountainRoadRidgeDescriptor>(20);
            float scenicXMin = plateau.BoundsXZ.xMin;
            float scenicXMax = plateau.BoundsXZ.xMax;
            float scenicZMin = plateau.BoundsXZ.yMin;
            float scenicZMax = plateau.BoundsXZ.yMax;
            for (int sampleIndex = 0;
                 sampleIndex < route.Samples.Count;
                 sampleIndex++)
            {
                Vector3 sample = route.Samples[sampleIndex].Position;
                scenicXMin = Mathf.Min(scenicXMin, sample.x);
                scenicXMax = Mathf.Max(scenicXMax, sample.x);
                scenicZMin = Mathf.Min(scenicZMin, sample.z);
                scenicZMax = Mathf.Max(scenicZMax, sample.z);
            }

            Rect scenicBounds = Rect.MinMaxRect(
                scenicXMin - TerrainMargin,
                scenicZMin - TerrainMargin,
                scenicXMax + TerrainMargin,
                scenicZMax + TerrainMargin);
            for (int index = 0; index < 8; index++)
            {
                float t = index / 7f;
                Vector3 center = index < 4
                    ? new Vector3(
                        Mathf.Lerp(scenicBounds.xMin, scenicBounds.xMax, t),
                        7f + index * 0.8f,
                        scenicBounds.yMax - 2f)
                    : new Vector3(
                        index % 2 == 0
                            ? scenicBounds.xMin + 2f
                            : scenicBounds.xMax - 2f,
                        8f + index * 0.55f,
                        Mathf.Lerp(scenicBounds.yMin, scenicBounds.yMax, t));
                result.Add(new MountainRoadRidgeDescriptor(
                    $"mid-ridge-{index:00}",
                    MountainRoadRidgeLayer.Mid,
                    center,
                    new Vector3(16f + (index % 3) * 4f, 13f, 8f),
                    index * 31f,
                    seed + index * 97));
            }

            for (int index = 0; index < 12; index++)
            {
                float angle = Mathf.Lerp(-120f, 120f, index / 11f) *
                              Mathf.Deg2Rad;
                float radius = 66f + (index % 3) * 6f;
                Vector3 direction = new Vector3(
                    Mathf.Sin(angle),
                    0f,
                    Mathf.Cos(angle));
                Vector3 center = plateau.Center + direction * radius;
                center.y = 19f + (index % 4) * 2.6f;
                bool cablewayOccluder = index == 7;
                if (cablewayOccluder)
                {
                    Vector3 cableEnd = terminal.Cableway.UpperCableCenter;
                    center = new Vector3(
                        cableEnd.x -
                        terminal.Cableway.LineForward.x * 1.8f,
                        28f,
                        cableEnd.z -
                        terminal.Cableway.LineForward.z * 1.8f);
                }

                result.Add(new MountainRoadRidgeDescriptor(
                    cablewayOccluder
                        ? terminal.Cableway.UpperOccluderStableId
                        : $"far-snow-ridge-{index:00}",
                    MountainRoadRidgeLayer.FarSnow,
                    center,
                    cablewayOccluder
                        ? new Vector3(30f, 33f, 10f)
                        : new Vector3(
                            25f + (index % 3) * 5f,
                            24f + (index % 4) * 3f,
                            10f),
                    cablewayOccluder
                        ? Mathf.Atan2(
                            -terminal.Cableway.LineForward.x,
                            -terminal.Cableway.LineForward.z) * Mathf.Rad2Deg
                        : Mathf.Atan2(-direction.x, -direction.z) *
                          Mathf.Rad2Deg,
                    cablewayOccluder
                        ? seed + 4099
                        : seed + 2000 + index * 131));
            }

            return result;
        }

        private static uint Hash(int seed, int index, uint salt)
        {
            uint hash = CitySoundStableHash.Combine(
                unchecked((uint)seed),
                unchecked((uint)index));
            return CitySoundStableHash.Combine(hash, salt);
        }

        private static float Unit(int seed, int index, uint salt)
        {
            return CitySoundStableHash.ToUnitFloat(Hash(seed, index, salt));
        }
    }
}
