using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class MountainRoadPlanner
    {
        public const int DefaultSeed = 19081987;
        public const float OutdoorRouteLength = 620f;
        public const float ElevationGain = 26.1f;
        public const float HairpinRadius = 7.5f;
        public const int HairpinCount = 10;
        public const float RoadWidth = 4.8f;
        public const float HairpinWidth = 6.4f;
        public const float MaximumGrade = 0.08f;
        public const float SpawnDepth = 6f;
        public const float TunnelVisualDepth = 9f;
        public const float TerrainMargin = 76f;
        public const float RidgeTerrainBurial = 1.5f;
        public const float RidgeRoadClearance = 1.5f;
        public const float RidgeTreeClearance = 0.75f;

        /// <summary>
        /// How far the cut takes the ground down. A constant, not a floor:
        /// the bed keeps the macro slope, so the far end of the terrain
        /// mesh is a lowered continuation rather than a second cliff.
        /// `26 m` puts the bed at roughly the height of the tunnel the
        /// hero drove out of, which is what the drop is measured against.
        /// </summary>
        public const float BrinkDropDepth = 26f;

        public const float BrinkEdgeBlendDistance = 4.5f;

        /// <summary>
        /// The middle of the measured gap, and a wedge narrow enough to
        /// keep its jambs standing. Swept from the rim, the ridges leave
        /// `-44` to `-10` degrees clear; centring on `-27` and opening
        /// `9` degrees plus `3` of taper leaves five degrees of margin on
        /// each side, which at their own distances is seven metres of
        /// footprint clearance rather than the two a wider slot left.
        /// It is a notch, not a panorama - and a notch is what a mountain
        /// gives you.
        /// </summary>
        public const float BrinkCorridorBearingDegrees = -27f;

        public const float BrinkCorridorHalfAngle = 9f;
        public const float BrinkCorridorTaper = 3f;
        public const float BrinkCorridorInnerRadius = 3f;

        /// <summary>
        /// Past the area's `120 m` far plane, so the far wall of the cut
        /// is never a visible end to it.
        /// </summary>
        public const float BrinkCorridorOuterRadius = 132f;

        /// <summary>How far every grounded thing stays out of the cut.</summary>
        public const float BrinkRouteClearance = 10f;
        public const float BrinkCablewayClearance = 6f;
        public const float BrinkRidgeClearance = 3f;
        public const float BrinkForestClearance = 1f;

        private const float BrinkRimStartOffset = -7f;
        private const float BrinkRimEndOffset = 13f;
        private const float BrinkRimForward = 18f;

        private const float LowerRunLength = 25f;
        private const float LowerShelfLength = 26f;
        private const float UpperShelfLength = 33f;
        private const int LowerHairpinCount = 5;
        private const float BridgeApproachLength = 10f;
        private const float BridgeLength = 50f;
        private const float PlateauEntryLead = 5f;
        private const float TerminalTerraceRun = 20f;
        private const float ForestRoadClearance = 0.75f;
        private const float RoadsidePropClearance = 0.8f;
        private const float MidRidgeEnvelopeOffset = 44f;
        private const float FarRidgeEnvelopeOffset = 62f;
        private const int RidgeGroundingStationCount = 6;

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
            List<MountainRoadRidgeDescriptor> ridges =
                CreateRidges(
                    seed,
                    route,
                    plateau,
                    terminal);
            List<MountainRoadForestDescriptor> forest =
                CreateForest(
                    seed,
                    route,
                    plateau,
                    terminal,
                    terrainBounds,
                    ridges);
            List<MountainRoadMiscDescriptor> misc =
                CreateMisc(seed, tunnel, route, plateau);
            List<MountainRoadSoundAnchor> sounds =
                CreateSoundAnchors(misc, terminal.Site);
            Bounds worldBounds = CalculateWorldBounds(
                terrainBounds,
                route,
                plateau,
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
                sounds,
                MountainRoadVistaPlanner.Create(
                    plateau,
                    terminal.Site,
                    seed));
            MountainRoadValidator.ValidateOrThrow(plan);
            return plan;
        }

        private static Bounds CalculateWorldBounds(
            Rect terrainBounds,
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau,
            MountainRoadTerminalPlan terminal,
            IReadOnlyList<MountainRoadRidgeDescriptor> ridges)
        {
            float minimumX = terrainBounds.xMin;
            float maximumX = terrainBounds.xMax;
            float brinkFloor = plateau.Brink == null
                ? 0f
                : SampleBrinkFloor(route, plateau);
            float minimumY = Mathf.Min(
                -12f,
                Mathf.Min(
                    route.Bridge.GorgeFloorY - 2f,
                    brinkFloor - 2f));
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
            var points = new List<MutablePoint>(720)
            {
                new MutablePoint(
                    0f,
                    Vector3.zero,
                    MountainRoadRouteSection.LowerClimb,
                    -1)
            };
            var hairpins = new List<MountainRoadHairpinDescriptor>(
                HairpinCount);
            Vector3 forward = Vector3.forward;

            AppendStraight(
                points,
                LowerRunLength,
                forward,
                MountainRoadRouteSection.LowerClimb);
            for (int index = 0; index < LowerHairpinCount; index++)
            {
                hairpins.Add(AppendHairpin(points, ref forward, index));
                if (index < LowerHairpinCount - 1)
                {
                    AppendStraight(
                        points,
                        LowerShelfLength,
                        forward,
                        MountainRoadRouteSection.LowerClimb);
                }
            }

            AppendStraight(
                points,
                BridgeApproachLength,
                forward,
                MountainRoadRouteSection.BridgeApproach);
            MutablePoint bridgeStart = points[points.Count - 1];
            AppendStraight(
                points,
                BridgeLength,
                forward,
                MountainRoadRouteSection.Bridge);
            MutablePoint bridgeEnd = points[points.Count - 1];
            var bridge = new MountainRoadBridgeDescriptor(
                "mountain-bridge",
                bridgeStart.Distance,
                bridgeEnd.Distance,
                bridgeStart.Position,
                bridgeEnd.Position,
                RoadWidth,
                5.8f,
                0.72f,
                1.1f,
                -16f,
                12f,
                6f);
            AppendStraight(
                points,
                BridgeApproachLength,
                forward,
                MountainRoadRouteSection.BridgeApproach);

            for (int index = LowerHairpinCount;
                 index < HairpinCount;
                 index++)
            {
                hairpins.Add(AppendHairpin(points, ref forward, index));
                if (index < HairpinCount - 1)
                {
                    AppendStraight(
                        points,
                        UpperShelfLength,
                        forward,
                        MountainRoadRouteSection.UpperClimb);
                }
            }

            float climbDistance = ClimbLength -
                                  points[points.Count - 1].Distance;
            AppendStraight(
                points,
                climbDistance,
                forward,
                MountainRoadRouteSection.UpperClimb);

            // The climb stops beside the last switchback, but the terminal
            // pad must not: its rim is a raised terrace and would bury the
            // outer arc of hairpin 8 in snow. The road leaves the switchback
            // field on a level terrace run before the plateau mouth.
            AppendStraight(
                points,
                TerminalTerraceRun + PlateauEntryLead,
                forward,
                MountainRoadRouteSection.UpperApproach);

            var samples = new List<MountainRoadRouteSample>(points.Count);
            for (int index = 0; index < points.Count; index++)
            {
                MutablePoint point = points[index];
                Vector3 tangent;
                if (index == 0)
                {
                    tangent = points[1].Position - point.Position;
                }
                else if (index == points.Count - 1)
                {
                    tangent = point.Position - points[index - 1].Position;
                }
                else
                {
                    tangent = points[index + 1].Position -
                              points[index - 1].Position;
                }

                tangent.y = 0f;
                tangent.Normalize();
                samples.Add(new MountainRoadRouteSample(
                    $"mountain-route-{index:000}",
                    point.Distance,
                    point.Position,
                    tangent,
                    EvaluateWidth(
                        point.Distance,
                        hairpins),
                    point.Section,
                    point.HairpinIndex));
            }

            return new MountainRoadRoutePlan(
                samples,
                OutdoorRouteLength,
                hairpins,
                bridge);
        }

        private static void AppendStraight(
            List<MutablePoint> target,
            float length,
            Vector3 forward,
            MountainRoadRouteSection section)
        {
            MutablePoint start = target[target.Count - 1];
            float endDistance = start.Distance + length;
            Vector3 end = start.Position + forward * length;
            end.y = EvaluateElevation(endDistance);
            int divisions = Mathf.Max(1, Mathf.CeilToInt(length));
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

        private static MountainRoadHairpinDescriptor AppendHairpin(
            List<MutablePoint> target,
            ref Vector3 forward,
            int hairpinIndex)
        {
            const int divisions = 24;
            MutablePoint start = target[target.Count - 1];
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            int turnSide = right.x >= 0f ? 1 : -1;
            Vector3 center = start.Position +
                             right * (turnSide * HairpinRadius);
            Vector3 radial = start.Position - center;
            radial.y = 0f;
            float endDistance = start.Distance + Mathf.PI * HairpinRadius;
            Vector3 apex = start.Position;
            for (int step = 1; step <= divisions; step++)
            {
                float t = step / (float)divisions;
                float distance = Mathf.Lerp(
                    start.Distance,
                    endDistance,
                    t);
                Vector3 offset = Quaternion.AngleAxis(
                    turnSide * 180f * t,
                    Vector3.up) * radial;
                Vector3 position = center + offset;
                position.y = EvaluateElevation(distance);
                target.Add(new MutablePoint(
                    distance,
                    position,
                    MountainRoadRouteSection.Hairpin,
                    hairpinIndex));
                if (step == divisions / 2)
                {
                    apex = position;
                }
            }

            forward = -forward;
            return new MountainRoadHairpinDescriptor(
                $"mountain-hairpin-{hairpinIndex:00}",
                hairpinIndex,
                start.Distance,
                endDistance,
                new Vector2(center.x, center.z),
                apex,
                turnSide);
        }

        private static float EvaluateWidth(
            float distance,
            IReadOnlyList<MountainRoadHairpinDescriptor> hairpins)
        {
            float weight = 0f;
            for (int index = 0; index < hairpins.Count; index++)
            {
                weight = Mathf.Max(
                    weight,
                    HairpinWeight(
                        distance,
                        hairpins[index].StartDistance,
                        hairpins[index].EndDistance));
            }

            return Mathf.Lerp(
                RoadWidth,
                HairpinWidth,
                weight);
        }

        private static float ClimbLength =>
            OutdoorRouteLength - TerminalTerraceRun - PlateauEntryLead;

        private static float EvaluateElevation(float distance)
        {
            float t = Mathf.Clamp01(distance / ClimbLength);
            return ElevationGain * Mathf.SmoothStep(0f, 1f, t);
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

        /// <summary>
        /// The lowest ground the cut reaches. Walked rather than derived,
        /// because the bed keeps the macro slope and the deepest point is
        /// wherever the slope has fallen furthest by the outer radius.
        /// </summary>
        private static float SampleBrinkFloor(
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau)
        {
            MountainRoadViewCorridor corridor = plateau.Brink.Corridor;
            float lowest = float.PositiveInfinity;
            const int stations = 24;
            for (int index = 0; index <= stations; index++)
            {
                float distance = Mathf.Lerp(
                    corridor.InnerRadius,
                    corridor.OuterRadius,
                    index / (float)stations);
                Vector3 point = corridor.Apex + corridor.Axis * distance;
                lowest = Mathf.Min(
                    lowest,
                    MountainRoadTerrainSampler.SampleHeight(
                        route,
                        plateau,
                        new Vector2(point.x, point.z)));
            }

            return lowest;
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
                vertices,
                CreateBrink(center, right, forward));
        }

        /// <summary>
        /// The back rim, and the one place the mountain is allowed to open.
        ///
        /// Both numbers were measured rather than chosen. The back band is
        /// the only stretch of this plateau with room for a terrace at all:
        /// the west flank leaves two metres between the cafe's blind wall
        /// and the rim, and the east is where the ground rises. And the
        /// bearing is the middle of the one wide sector with no ridge
        /// inside the area's own far plane - swept from the rim, the
        /// mid and far ridges stand shoulder to shoulder from `-60` to
        /// `-46` degrees and again from `-8` through `+14`, and between
        /// those two masses there is nothing at all out to `120 m`. So the
        /// cut does not need a ridge moved out of its way; the ridges
        /// already part here, and the opening is aimed at the gap they
        /// leave. They become its jambs.
        /// </summary>
        private static MountainRoadBrinkDescriptor CreateBrink(
            Vector3 center,
            Vector3 right,
            Vector3 forward)
        {
            Vector3 rimStart = center +
                               right * BrinkRimStartOffset +
                               forward * BrinkRimForward;
            Vector3 rimEnd = center +
                             right * BrinkRimEndOffset +
                             forward * BrinkRimForward;
            float bearing = BrinkCorridorBearingDegrees * Mathf.Deg2Rad;
            Vector3 axis = (
                right * Mathf.Sin(bearing) +
                forward * Mathf.Cos(bearing)).normalized;
            var corridor = new MountainRoadViewCorridor(
                (rimStart + rimEnd) * 0.5f,
                axis,
                BrinkCorridorHalfAngle,
                BrinkCorridorTaper,
                BrinkCorridorInnerRadius,
                BrinkCorridorOuterRadius);
            return new MountainRoadBrinkDescriptor(
                rimStart,
                rimEnd,
                forward,
                BrinkDropDepth,
                BrinkEdgeBlendDistance,
                corridor);
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
            Rect terrainBounds,
            IReadOnlyList<MountainRoadRidgeDescriptor> ridges)
        {
            var result = new List<MountainRoadForestDescriptor>(420);
            AppendForestLayer(
                result,
                seed,
                route,
                plateau,
                terminal,
                terrainBounds,
                ridges,
                MountainRoadForestLayer.Physical,
                92,
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
                ridges,
                MountainRoadForestLayer.Mid,
                142,
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
                ridges,
                MountainRoadForestLayer.Far,
                186,
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
            IReadOnlyList<MountainRoadRidgeDescriptor> ridges,
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
                if (sample.IsBridge &&
                    layer != MountainRoadForestLayer.Far)
                {
                    continue;
                }

                float progress = distance / route.Length;
                float upperRetention;
                switch (layer)
                {
                    case MountainRoadForestLayer.Physical:
                        upperRetention = 0.28f;
                        break;
                    case MountainRoadForestLayer.Mid:
                        upperRetention = 0.48f;
                        break;
                    case MountainRoadForestLayer.Far:
                        upperRetention = 0.72f;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(layer));
                }

                float retention = Mathf.Lerp(
                    1f,
                    upperRetention,
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                        0.54f,
                        0.94f,
                        progress)));
                if (Unit(seed, attempt, 0x54524545u) > retention)
                {
                    continue;
                }

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
                    MountainRoadCompositionRules.IsReservedForestOpening(
                        route,
                        plateau,
                        layer,
                        point,
                        radius) ||
                    IntersectsRidgeFootprint(point, radius, ridges) ||
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

        private static bool IntersectsRidgeFootprint(
            Vector2 point,
            float crownRadius,
            IReadOnlyList<MountainRoadRidgeDescriptor> ridges)
        {
            float clearance = crownRadius + RidgeTreeClearance;
            for (int index = 0; index < ridges.Count; index++)
            {
                if (MountainRoadRidgeGeometry.DistanceToFootprint(
                        point,
                        ridges[index]) < clearance)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<MountainRoadMiscDescriptor> CreateMisc(
            int seed,
            MountainRoadTunnelDescriptor tunnel,
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau)
        {
            var result = new List<MountainRoadMiscDescriptor>(180);
            AddAuthoredRoadsideObjects(result, tunnel, route, plateau);

            // Pack the tallest/widest imported silhouettes first. Their
            // visible branches scale by height, so leaving them until after
            // a hundred small props can exhaust every bounded candidate.
            const int deadTreeCount = 16;
            for (int index = 0; index < deadTreeCount; index++)
            {
                const uint placementSalt = 0x44454144u;
                result.Add(MountainRoadCompositionRules.PlaceNaturalMisc(
                    $"misc-dead-tree-{index:00}",
                    MountainRoadMiscKind.DeadTree,
                    route,
                    plateau,
                    result,
                    seed,
                    index,
                    placementSalt,
                    7.1f,
                    9.2f,
                    new Vector3(0.72f, 8.2f + index * 0.45f, 0.72f),
                    true));
            }

            const int boulderCount = 54;
            for (int index = 0; index < boulderCount; index++)
            {
                const uint placementSalt = 0x424F554Cu;
                Vector3 size = new Vector3(
                    1.2f + Unit(seed, index, 0x42585A21u) * 1.8f,
                    0.8f + Unit(seed, index, 0x42592121u) * 1.3f,
                    1.2f + Unit(seed, index, 0x42585A22u) * 1.8f);
                result.Add(MountainRoadCompositionRules.PlaceNaturalMisc(
                    $"misc-boulder-{index:00}",
                    MountainRoadMiscKind.Boulder,
                    route,
                    plateau,
                    result,
                    seed,
                    index,
                    placementSalt,
                    4.2f,
                    8.2f,
                    size,
                    true));
            }

            const int logCount = 24;
            for (int index = 0; index < logCount; index++)
            {
                const uint placementSalt = 0x4C4F4721u;
                result.Add(MountainRoadCompositionRules.PlaceNaturalMisc(
                    $"misc-fallen-log-{index:00}",
                    MountainRoadMiscKind.FallenLog,
                    route,
                    plateau,
                    result,
                    seed,
                    index,
                    placementSalt,
                    5.9f,
                    8.7f,
                    new Vector3(0.68f, 0.68f, 4.6f + (index % 2) * 0.9f),
                    true));
            }

            const int stumpCount = 28;
            for (int index = 0; index < stumpCount; index++)
            {
                const uint placementSalt = 0x5354554Du;
                result.Add(MountainRoadCompositionRules.PlaceNaturalMisc(
                    $"misc-stump-{index:00}",
                    MountainRoadMiscKind.Stump,
                    route,
                    plateau,
                    result,
                    seed,
                    index,
                    placementSalt,
                    5.1f,
                    8f,
                    new Vector3(0.88f, 1.05f, 0.88f),
                    true));
            }

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
                route.Length * 0.10f,
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
                route.Hairpins[0].StartDistance + 1.3f,
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
                route.Length * 0.66f,
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
                route.Length * 0.67f,
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
                MountainRoadCompositionRules.AbandonedChairDistance(route),
                -1f,
                4.1f,
                new Vector3(0.82f, 1.1f, 0.82f),
                true,
                172f));

            for (int index = 0; index < route.Hairpins.Count; index++)
            {
                MountainRoadHairpinDescriptor hairpin =
                    route.Hairpins[index];
                result.Add(PlaceMisc(
                    $"misc-guardrail-{index}",
                    MountainRoadMiscKind.GuardRail,
                    route,
                    plateau,
                    hairpin.StartDistance + 4.2f,
                    -hairpin.TurnSide,
                    4.2f,
                    new Vector3(0.22f, 1.05f, 6.4f),
                    true,
                    0f));
            }

            MountainRoadBridgeDescriptor bridge = route.Bridge;
            Vector3 looseRailPosition = bridge.Center +
                bridge.Right * (bridge.DeckWidth * 0.5f - 0.12f) +
                Vector3.up * (bridge.RailHeight * 0.5f);
            result.Add(new MountainRoadMiscDescriptor(
                "misc-bridge-loose-rail",
                MountainRoadMiscKind.GuardRail,
                looseRailPosition,
                Quaternion.LookRotation(bridge.Forward, Vector3.up),
                new Vector3(0.22f, bridge.RailHeight, 6.4f),
                true));

            const int snowPoleCount = 20;
            for (int index = 0; index < snowPoleCount; index++)
            {
                result.Add(PlaceMisc(
                    $"misc-snow-pole-{index}",
                    MountainRoadMiscKind.SnowPole,
                    route,
                    plateau,
                    route.Length * 0.74f +
                    index * (route.Length * 0.20f /
                             (snowPoleCount - 1f)),
                    (index & 1) == 0 ? -1f : 1f,
                    3.5f,
                    new Vector3(0.14f, 3f, 0.14f),
                    false,
                    0f));
            }
        }

        internal static MountainRoadMiscDescriptor PlaceMisc(
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

        /// <summary>
        /// Every sound on this mountain answers to something you can
        /// see. The five on the road are road furniture; the four on
        /// the summit are the yard lamp's ballast, the loose pipe in
        /// the gap of the parapet, the halyard on the windsock mast
        /// and the tarp over the freight - and the last two are the
        /// same wind that bends the trees, arriving at two different
        /// materials.
        /// </summary>
        private static List<MountainRoadSoundAnchor> CreateSoundAnchors(
            IReadOnlyList<MountainRoadMiscDescriptor> misc,
            MountainRoadTerminalSitePlan site)
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
                    "misc-bridge-loose-rail", 7f),
                CreateSound(byId, "sound-utility-cable",
                    MountainRoadSoundAnchorKind.UtilityCable,
                    "misc-utility-cable", 8f),
                CreateSound(byId, "sound-snow-pole",
                    MountainRoadSoundAnchorKind.SnowPole,
                    "misc-snow-pole-16", 6f),
                CreateSitePartSound(site, "sound-yard-lamp-ballast",
                    MountainRoadSoundAnchorKind.TunnelLampBallast,
                    "site-yard-lamp-shade", 6.5f),
                CreateSitePartSound(site, "sound-parapet-gap-rail",
                    MountainRoadSoundAnchorKind.LooseGuardRail,
                    "site-parapet-gap-post-00", 7f),
                CreateClothSound(site, "sound-windsock-halyard",
                    MountainRoadSoundAnchorKind.WindsockHalyard,
                    "site-windsock", 8f),
                CreateClothSound(site, "sound-load-tarp",
                    MountainRoadSoundAnchorKind.LoadTarp,
                    "site-load-tarp", 6f)
            };
        }

        private static MountainRoadSoundAnchor CreateSitePartSound(
            MountainRoadTerminalSitePlan site,
            string stableId,
            MountainRoadSoundAnchorKind kind,
            string sourceId,
            float radius)
        {
            if (!site.TryGetPart(
                    sourceId,
                    out MountainRoadSitePartDescriptor part))
            {
                throw new InvalidOperationException(
                    $"The site has no '{sourceId}' to sound from.");
            }

            return new MountainRoadSoundAnchor(
                stableId,
                kind,
                sourceId,
                part.Center,
                radius);
        }

        private static MountainRoadSoundAnchor CreateClothSound(
            MountainRoadTerminalSitePlan site,
            string stableId,
            MountainRoadSoundAnchorKind kind,
            string sourceId,
            float radius)
        {
            for (int index = 0; index < site.Cloth.Count; index++)
            {
                if (!string.Equals(
                        site.Cloth[index].StableId,
                        sourceId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                return new MountainRoadSoundAnchor(
                    stableId,
                    kind,
                    sourceId,
                    site.Cloth[index].Anchor,
                    radius);
            }

            throw new InvalidOperationException(
                $"The site has no '{sourceId}' to sound from.");
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
                scenicXMin,
                scenicZMin,
                scenicXMax,
                scenicZMax);
            for (int index = 0; index < 8; index++)
            {
                Vector2 center = SampleExpandedPerimeter(
                    scenicBounds,
                    MidRidgeEnvelopeOffset,
                    (index + 0.18f) / 8f,
                    out float tangentYaw);
                Vector3 size = new Vector3(
                    20f + (index % 3) * 5f,
                    18f,
                    10f);
                float yaw = tangentYaw +
                            (Unit(seed, index, 0x4D494459u) - 0.5f) * 14f;
                result.Add(CreateGroundedRidge(
                    $"mid-ridge-{index:00}",
                    MountainRoadRidgeLayer.Mid,
                    center,
                    size,
                    yaw,
                    route,
                    plateau,
                    seed + index * 97));
            }

            const int genericSnowRidgeCount = 11;
            for (int index = 0; index < genericSnowRidgeCount; index++)
            {
                Vector2 center = SampleExpandedPerimeter(
                    scenicBounds,
                    FarRidgeEnvelopeOffset,
                    (index + 0.33f) / genericSnowRidgeCount,
                    out float tangentYaw);
                Vector3 size = new Vector3(
                    25f + (index % 3) * 5f,
                    24f + (index % 4) * 3f,
                    10f);
                float yaw = tangentYaw +
                            (Unit(seed, index, 0x46415259u) - 0.5f) * 12f;
                result.Add(CreateGroundedRidge(
                    $"far-snow-ridge-{index:00}",
                    MountainRoadRidgeLayer.FarSnow,
                    center,
                    size,
                    yaw,
                    route,
                    plateau,
                    seed + 2000 + index * 131));
            }

            // Nothing stands across the line. The rope runs on past the
            // draw range now - there is no rock at the top and no building,
            // because there is no top to see - so a perimeter ridge whose
            // drawn crest would reach into the cabin's path is simply not
            // built: the rings part where the line goes through, which is
            // what a pass looks like from a cabin.
            MountainRoadCablewayPlan cableway = terminal.Cableway;
            for (int index = result.Count - 1; index >= 0; index--)
            {
                if (StandsAcrossTheLine(result[index], cableway))
                {
                    result.RemoveAt(index);
                }
            }

            return result;
        }

        /// <summary>Clear air a ridge's drawn crest must leave under the
        /// cabin's underside wherever the ridge's footprint holds a track
        /// point.</summary>
        public const float RidgeCabinClearance = 3f;

        /// <summary>
        /// Whether a ridge's drawn crest reaches into the band a cabin sweeps
        /// along either track. Against the crest that is BUILT, not the box
        /// it is authored in, and sampled every metre of the line.
        /// </summary>
        public static bool StandsAcrossTheLine(
            MountainRoadRidgeDescriptor ridge,
            MountainRoadCablewayPlan cableway)
        {
            for (float distance = 0f;
                 distance <= cableway.LineLength;
                 distance += 1f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 attachment =
                        MountainCablewayMotion.SampleTrackPosition(
                            cableway,
                            distance,
                            side);
                    if (!MountainRoadRidgeGeometry.TryGetCrossing(
                            ridge,
                            attachment,
                            out float amount))
                    {
                        continue;
                    }

                    float crest = MountainRoadRidgeGeometry.CrestWorldY(
                        ridge,
                        amount);
                    if (crest > attachment.y -
                        cableway.CabinAttachmentToBottom -
                        RidgeCabinClearance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static MountainRoadRidgeDescriptor CreateGroundedRidge(
            string stableId,
            MountainRoadRidgeLayer layer,
            Vector2 centerXZ,
            Vector3 size,
            float yawDegrees,
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau,
            int ridgeSeed)
        {
            float baseY = CalculateRidgeBaseY(
                route,
                plateau,
                centerXZ,
                size,
                yawDegrees);
            return new MountainRoadRidgeDescriptor(
                stableId,
                layer,
                new Vector3(
                    centerXZ.x,
                    baseY + size.y * 0.5f,
                    centerXZ.y),
                size,
                yawDegrees,
                ridgeSeed);
        }

        internal static float CalculateRidgeBaseY(
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau,
            Vector2 centerXZ,
            Vector3 size,
            float yawDegrees)
        {
            Quaternion rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            float minimumGround = MountainRoadTerrainSampler.SampleHeight(
                route,
                plateau,
                centerXZ);
            for (int depth = 0; depth < 2; depth++)
            {
                float localZ = (depth == 0 ? -0.5f : 0.5f) * size.z;
                for (int station = 0;
                     station < RidgeGroundingStationCount;
                     station++)
                {
                    float localX = Mathf.Lerp(
                        -0.5f,
                        0.5f,
                        station / (float)(RidgeGroundingStationCount - 1)) *
                        size.x;
                    Vector3 worldOffset = rotation * new Vector3(
                        localX,
                        0f,
                        localZ);
                    minimumGround = Mathf.Min(
                        minimumGround,
                        MountainRoadTerrainSampler.SampleHeight(
                            route,
                            plateau,
                            centerXZ + new Vector2(
                                worldOffset.x,
                                worldOffset.z)));
                }
            }

            return minimumGround - RidgeTerrainBurial;
        }

        private static Vector2 SampleExpandedPerimeter(
            Rect bounds,
            float offset,
            float normalizedDistance,
            out float tangentYaw)
        {
            float minimumX = bounds.xMin - offset;
            float maximumX = bounds.xMax + offset;
            float minimumZ = bounds.yMin - offset;
            float maximumZ = bounds.yMax + offset;
            float width = maximumX - minimumX;
            float depth = maximumZ - minimumZ;
            float perimeter = (width + depth) * 2f;
            float distance = Mathf.Repeat(normalizedDistance, 1f) * perimeter;
            if (distance < width)
            {
                tangentYaw = 0f;
                return new Vector2(minimumX + distance, minimumZ);
            }

            distance -= width;
            if (distance < depth)
            {
                tangentYaw = 90f;
                return new Vector2(maximumX, minimumZ + distance);
            }

            distance -= depth;
            if (distance < width)
            {
                tangentYaw = 0f;
                return new Vector2(maximumX - distance, maximumZ);
            }

            distance -= width;
            tangentYaw = 90f;
            return new Vector2(minimumX, maximumZ - distance);
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
