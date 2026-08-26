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
        private const float PlateauRoadClearance = 4f;

        public static void ValidateOrThrow(MountainRoadPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            ValidateTunnel(plan);
            ValidateRoute(plan.Route);
            ValidatePlateau(plan);
            ValidateTerminalPadClearsTheClimb(plan);
            ValidateForest(plan);
            ValidateMiscAndSounds(plan);
            ValidateRidges(plan);
            ValidateBrink(plan);
            ValidateBounds(plan);
            MountainRoadTerminalValidator.ValidateOrThrow(plan);
        }

        /// <summary>
        /// The cut takes ground away, and every rule here names one thing
        /// that would rather it did not.
        ///
        /// The cableway rule is the one worth reading twice. Lowering the
        /// ground under the line only INCREASES the clearance its own test
        /// measures, so a cut that put three supports on twenty-metre
        /// stilts would make that test greener, not redder. It is checked
        /// as a horizontal distance for exactly that reason.
        /// </summary>
        private static void ValidateBrink(MountainRoadPlan plan)
        {
            MountainRoadBrinkDescriptor brink = plan.Plateau.Brink;
            if (brink == null)
            {
                throw new InvalidOperationException(
                    "The terminal plateau needs its brink.");
            }

            RequireFinite(brink.RimStart, "Brink rim start");
            RequireFinite(brink.RimEnd, "Brink rim end");
            RequireNormalized(brink.Outward, "Brink outward");
            RequireNormalized(brink.Corridor.Axis, "Brink corridor axis");
            if (brink.DropDepth <= 1f ||
                brink.EdgeBlendDistance <= 0.5f ||
                brink.Corridor.HalfAngleDegrees <= 0f ||
                brink.Corridor.OuterHalfAngleDegrees >= 90f ||
                brink.Corridor.OuterRadius <=
                    brink.Corridor.InnerRadius + brink.EdgeBlendDistance)
            {
                throw new InvalidOperationException(
                    "The brink corridor is not a usable wedge.");
            }

            MountainRoadViewCorridor corridor = brink.Corridor;
            IReadOnlyList<MountainRoadRouteSample> samples =
                plan.Route.Samples;
            for (int index = 0; index < samples.Count; index++)
            {
                Vector3 position = samples[index].Position;
                RequireOutsideCorridor(
                    corridor,
                    new Vector2(position.x, position.z),
                    MountainRoadPlanner.BrinkRouteClearance,
                    $"Route sample '{samples[index].StableId}'");
            }

            IReadOnlyList<MountainCablewayNodeDescriptor> nodes =
                plan.Terminal.Cableway.Nodes;
            for (int index = 0; index < nodes.Count; index++)
            {
                Vector3 ground = nodes[index].GroundPosition;
                RequireOutsideCorridor(
                    corridor,
                    new Vector2(ground.x, ground.z),
                    MountainRoadPlanner.BrinkCablewayClearance,
                    $"Cableway node '{nodes[index].StableId}' ground");
            }

            for (int index = 0; index < plan.Ridges.Count; index++)
            {
                MountainRoadRidgeDescriptor ridge = plan.Ridges[index];
                float radians = ridge.YawDegrees * Mathf.Deg2Rad;
                var right = new Vector2(
                    Mathf.Cos(radians),
                    -Mathf.Sin(radians));
                var forward = new Vector2(
                    Mathf.Sin(radians),
                    Mathf.Cos(radians));
                var center = new Vector2(ridge.Center.x, ridge.Center.z);
                for (int cornerX = -1; cornerX <= 1; cornerX += 2)
                {
                    for (int cornerZ = -1; cornerZ <= 1; cornerZ += 2)
                    {
                        Vector2 corner = center +
                            right * (cornerX * ridge.Size.x * 0.5f) +
                            forward * (cornerZ * ridge.Size.z * 0.5f);
                        RequireOutsideCorridor(
                            corridor,
                            corner,
                            MountainRoadPlanner.BrinkRidgeClearance,
                            $"Ridge '{ridge.StableId}' footprint");
                    }
                }
            }

            for (int index = 0; index < plan.Forest.Count; index++)
            {
                Vector3 position = plan.Forest[index].Position;
                RequireOutsideCorridor(
                    corridor,
                    new Vector2(position.x, position.z),
                    MountainRoadPlanner.BrinkForestClearance +
                    plan.Forest[index].CrownRadius,
                    $"Tree '{plan.Forest[index].StableId}'");
            }
        }

        private static void RequireOutsideCorridor(
            MountainRoadViewCorridor corridor,
            Vector2 point,
            float margin,
            string label)
        {
            float depth = corridor.DepthInside(point);
            if (depth > -margin)
            {
                throw new InvalidOperationException(
                    $"{label} stands inside the brink corridor: it clears " +
                    $"the wedge by {(-depth):0.00} m against the " +
                    $"{margin:0.00} m the cut needs.");
            }
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
            if (route == null || route.Samples.Count < 500)
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

            if (route.Hairpins.Count != MountainRoadPlanner.HairpinCount)
            {
                throw new InvalidOperationException(
                    "Mountain road must expose every authored hairpin.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var hairpinSamples = new int[route.Hairpins.Count];
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
                    MountainRoadRouteSample previous =
                        route.Samples[index - 1];
                    float planar = Vector2.Distance(
                        ToXZ(previous.Position),
                        ToXZ(sample.Position));
                    if (planar <= 0.0001f)
                    {
                        throw new InvalidOperationException(
                            "Route samples must advance in the XZ plane.");
                    }

                    float grade = Mathf.Abs(
                        sample.Position.y - previous.Position.y) / planar;
                    if (grade > MountainRoadPlanner.MaximumGrade + 0.002f)
                    {
                        throw new InvalidOperationException(
                            $"{sample.StableId} exceeds the gradual-climb " +
                            $"grade: {grade:P1}.");
                    }
                }

                if (sample.HairpinIndex >= 0 &&
                    sample.HairpinIndex < hairpinSamples.Length)
                {
                    hairpinSamples[sample.HairpinIndex]++;
                }
                else if (sample.HairpinIndex != -1)
                {
                    throw new InvalidOperationException(
                        "Route sample references an unknown hairpin.");
                }

                previousDistance = sample.Distance;
                previousY = sample.Position.y;
            }

            if (maximumGap > 1.08f)
            {
                throw new InvalidOperationException(
                    "Route sampling is too sparse for a continuous ribbon.");
            }

            var hairpinIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < route.Hairpins.Count; index++)
            {
                MountainRoadHairpinDescriptor hairpin = route.Hairpins[index];
                if (hairpin.Index != index ||
                    string.IsNullOrWhiteSpace(hairpin.StableId) ||
                    !hairpinIds.Add(hairpin.StableId) ||
                    hairpinSamples[index] < 17)
                {
                    throw new InvalidOperationException(
                        "Hairpin descriptors must be ordered, unique and " +
                        "densely sampled.");
                }

                ValidateHairpin(route, hairpin);
            }

            ValidateBridge(route);
            ValidateNoNonAdjacentRoadOverlap(route);
        }

        private static void ValidateHairpin(
            MountainRoadRoutePlan route,
            MountainRoadHairpinDescriptor hairpin)
        {
            RequireApproximately(
                hairpin.EndDistance - hairpin.StartDistance,
                Mathf.PI * MountainRoadPlanner.HairpinRadius,
                $"Hairpin {hairpin.Index} arc length");
            MountainRoadRouteSample start = route.Sample(
                hairpin.StartDistance);
            MountainRoadRouteSample middle = route.Sample(
                (hairpin.StartDistance + hairpin.EndDistance) * 0.5f);
            MountainRoadRouteSample end = route.Sample(
                hairpin.EndDistance);
            RequireApproximately(
                Vector2.Distance(
                    new Vector2(middle.Position.x, middle.Position.z),
                    hairpin.CenterXZ),
                MountainRoadPlanner.HairpinRadius,
                $"Hairpin {hairpin.Index} radius");
            if (Vector3.Distance(middle.Position, hairpin.ApexPosition) >
                PositionTolerance)
            {
                throw new InvalidOperationException(
                    $"Hairpin {hairpin.Index} apex detached from route.");
            }

            if (Vector3.Dot(start.Forward, end.Forward) > -0.98f)
            {
                throw new InvalidOperationException(
                    $"Hairpin {hairpin.Index} does not reverse the road.");
            }

            if (middle.Width < MountainRoadPlanner.HairpinWidth - 0.01f)
            {
                throw new InvalidOperationException(
                    $"Hairpin {hairpin.Index} is not widened at its apex.");
            }
        }

        private static void ValidateBridge(MountainRoadRoutePlan route)
        {
            MountainRoadBridgeDescriptor bridge = route.Bridge;
            RequireFinite(bridge.Start, "Bridge start");
            RequireFinite(bridge.End, "Bridge end");
            RequireFinite(bridge.Center, "Bridge center");
            RequireNormalized(bridge.Forward, "Bridge forward");
            RequireNormalized(bridge.Right, "Bridge right");
            if (string.IsNullOrWhiteSpace(bridge.StableId) ||
                bridge.Length < 45f ||
                bridge.Length > 55f ||
                bridge.ClearWidth < MountainRoadPlanner.RoadWidth - 0.01f ||
                bridge.DeckWidth < bridge.ClearWidth + 0.5f ||
                bridge.DeckThickness < 0.5f ||
                bridge.RailHeight < 1f ||
                Mathf.Min(bridge.Start.y, bridge.End.y) -
                    bridge.GorgeFloorY < 25f ||
                bridge.GorgeHalfWidth < 10f ||
                bridge.AbutmentBlendLength < 4f)
            {
                throw new InvalidOperationException(
                    "Mountain bridge lacks its automotive deck or high gorge.");
            }

            MountainRoadRouteSample start = route.Sample(
                bridge.StartDistance);
            MountainRoadRouteSample end = route.Sample(bridge.EndDistance);
            if (Vector3.Distance(start.Position, bridge.Start) >
                    PositionTolerance ||
                Vector3.Distance(end.Position, bridge.End) > PositionTolerance)
            {
                throw new InvalidOperationException(
                    "Mountain bridge endpoints detached from the route.");
            }

            Vector2 bridgeStart = ToXZ(bridge.Start);
            Vector2 bridgeForward = new Vector2(
                bridge.Forward.x,
                bridge.Forward.z);
            for (int index = 0; index < route.Samples.Count; index++)
            {
                MountainRoadRouteSample sample = route.Samples[index];
                if (sample.Distance <= bridge.StartDistance + 0.001f ||
                    sample.Distance > bridge.EndDistance + 0.001f)
                {
                    continue;
                }

                Vector2 delta = ToXZ(sample.Position) - bridgeStart;
                float along = Vector2.Dot(delta, bridgeForward);
                Vector2 lateral = delta - bridgeForward * along;
                if (sample.Section != MountainRoadRouteSection.Bridge ||
                    lateral.magnitude > PositionTolerance ||
                    sample.Width < bridge.ClearWidth - 0.01f)
                {
                    throw new InvalidOperationException(
                        "Bridge route must stay straight, continuous and wide.");
                }
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

        /// <summary>
        /// The terminal pad is a raised terrace, and the terrain sampler
        /// snaps every point inside it to the pad height. Where the rim
        /// reaches across the climbing ribbon the road is therefore buried
        /// under a metre and a half of collidered snow, with the asphalt
        /// still drawn underneath it - the car simply stops. Only the pad's
        /// own approach may touch the road; the switchbacks must stay clear.
        /// </summary>
        private static void ValidateTerminalPadClearsTheClimb(
            MountainRoadPlan plan)
        {
            IReadOnlyList<Vector2> rim = plan.Plateau.VerticesXZ;
            IReadOnlyList<MountainRoadRouteSample> samples =
                plan.Route.Samples;
            for (int index = 1; index < samples.Count; index++)
            {
                MountainRoadRouteSample first = samples[index - 1];
                MountainRoadRouteSample second = samples[index];
                if (first.Section ==
                        MountainRoadRouteSection.UpperApproach ||
                    second.Section ==
                        MountainRoadRouteSection.UpperApproach)
                {
                    continue;
                }

                Vector2 start = ToXZ(first.Position);
                Vector2 end = ToXZ(second.Position);
                float required = Mathf.Max(first.Width, second.Width) * 0.5f +
                                 PlateauRoadClearance;
                for (int edge = 0; edge < rim.Count; edge++)
                {
                    float distance = SegmentDistance(
                        start,
                        end,
                        rim[edge],
                        rim[(edge + 1) % rim.Count]);
                    if (distance >= required)
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        "The terminal plateau reaches across the climbing " +
                        $"road at {first.StableId}: rim edge {edge} is " +
                        $"{distance:0.###} m away, {required:0.###} m is " +
                        "required, and the road there would be buried " +
                        "under the pad.");
                }
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

            if (physical < 88 || physical > 100 ||
                mid < 135 || mid > 155 ||
                far < 180 || far > 200)
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
                    !TryFindSoundSource(
                        plan,
                        byId,
                        sound.SourceObjectStableId,
                        out Vector3 sourcePosition))
                {
                    throw new InvalidOperationException(
                        "Every sound anchor needs a unique ID and visible source.");
                }

                if (Vector3.Distance(sound.Position, sourcePosition) > 0.01f ||
                    sound.AudibleRadius < 4f ||
                    sound.AudibleRadius > 10f)
                {
                    throw new InvalidOperationException(
                        $"{sound.StableId} is detached from its visible source.");
                }
            }

            if (plan.SoundAnchors.Count != 9)
            {
                throw new InvalidOperationException(
                    "The authored area exposes exactly nine causal " +
                    "sound anchors: five on the road, four on the summit.");
            }
        }

        /// <summary>
        /// A visible source is roadside furniture OR a piece of the
        /// summit - a part inside a batch, or one of the two cloths.
        /// The rule was never about which list a thing lives in; it is
        /// that a sound here has something you can walk up to and look
        /// at.
        /// </summary>
        private static bool TryFindSoundSource(
            MountainRoadPlan plan,
            IReadOnlyDictionary<string, MountainRoadMiscDescriptor> misc,
            string sourceId,
            out Vector3 position)
        {
            if (misc.TryGetValue(
                    sourceId,
                    out MountainRoadMiscDescriptor item))
            {
                position = item.Position;
                return true;
            }

            MountainRoadTerminalSitePlan site = plan.Terminal.Site;
            if (site != null)
            {
                if (site.TryGetPart(
                        sourceId,
                        out MountainRoadSitePartDescriptor part))
                {
                    position = part.Center;
                    return true;
                }

                for (int index = 0; index < site.Cloth.Count; index++)
                {
                    if (string.Equals(
                            site.Cloth[index].StableId,
                            sourceId,
                            StringComparison.Ordinal))
                    {
                        position = site.Cloth[index].Anchor;
                        return true;
                    }
                }
            }

            position = Vector3.zero;
            return false;
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
                float expectedBase = MountainRoadPlanner.CalculateRidgeBaseY(
                    plan.Route,
                    plan.Plateau,
                    ToXZ(ridge.Center),
                    ridge.Size,
                    ridge.YawDegrees);
                RequireApproximately(
                    ridge.Center.y - ridge.Size.y * 0.5f,
                    expectedBase,
                    ridge.StableId + " terrain-grounded base");
                ValidateRidgeClearance(plan, ridge);
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

        private static void ValidateRidgeClearance(
            MountainRoadPlan plan,
            MountainRoadRidgeDescriptor ridge)
        {
            for (int index = 0; index < plan.Route.Samples.Count; index++)
            {
                MountainRoadRouteSample sample = plan.Route.Samples[index];
                float required = sample.Width * 0.5f +
                                 MountainRoadPlanner.RidgeRoadClearance;
                if (MountainRoadRidgeGeometry.DistanceToFootprint(
                        ToXZ(sample.Position),
                        ridge) + PositionTolerance < required)
                {
                    throw new InvalidOperationException(
                        $"{ridge.StableId} intersects the playable road " +
                        $"near {sample.StableId}.");
                }
            }

            if (MountainRoadRidgeGeometry.DistanceToFootprint(
                    ToXZ(plan.Plateau.Center),
                    ridge) < MountainRoadPlanner.RidgeRoadClearance)
            {
                throw new InvalidOperationException(
                    $"{ridge.StableId} intersects the terminal plateau.");
            }

            for (int index = 0;
                 index < plan.Plateau.VerticesXZ.Count;
                 index++)
            {
                if (MountainRoadRidgeGeometry.DistanceToFootprint(
                        plan.Plateau.VerticesXZ[index],
                        ridge) < MountainRoadPlanner.RidgeRoadClearance)
                {
                    throw new InvalidOperationException(
                        $"{ridge.StableId} intersects the terminal plateau.");
                }
            }

            for (int index = 0; index < plan.Forest.Count; index++)
            {
                MountainRoadForestDescriptor tree = plan.Forest[index];
                float required = tree.CrownRadius +
                                 MountainRoadPlanner.RidgeTreeClearance;
                if (MountainRoadRidgeGeometry.DistanceToFootprint(
                        ToXZ(tree.Position),
                        ridge) + PositionTolerance < required)
                {
                    throw new InvalidOperationException(
                        $"{ridge.StableId} overlaps the crown of " +
                        $"{tree.StableId}.");
                }
            }
        }

        private static void ValidateBounds(MountainRoadPlan plan)
        {
            if (plan.TerrainBoundsXZ.width < 50f ||
                plan.TerrainBoundsXZ.height < 50f ||
                !Contains(plan.WorldBounds, plan.SpawnPosition) ||
                !Contains(plan.WorldBounds, plan.Route.End) ||
                !Contains(
                    plan.WorldBounds,
                    new Vector3(
                        plan.Bridge.Center.x,
                        plan.Bridge.GorgeFloorY,
                        plan.Bridge.Center.z)))
            {
                throw new InvalidOperationException(
                    "Mountain-road world bounds do not contain the playable area.");
            }

            for (int index = 0; index < plan.Route.Samples.Count; index++)
            {
                Vector3 sample = plan.Route.Samples[index].Position;
                if (!Contains(plan.WorldBounds, sample) ||
                    !plan.TerrainBoundsXZ.Contains(ToXZ(sample)))
                {
                    throw new InvalidOperationException(
                        $"Mountain-road bounds omit route sample {index}.");
                }
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
                MountainRoadRidgeDescriptor ridge = plan.Ridges[index];
                if (!ContainsRidgeEnvelope(plan.WorldBounds, ridge))
                {
                    throw new InvalidOperationException(
                        "Mountain-road bounds omit a mountain ridge.");
                }

                if (!ContainsRidgeFootprint(plan.TerrainBoundsXZ, ridge))
                {
                    throw new InvalidOperationException(
                        "Mountain terrain does not continue below a ridge.");
                }
            }
        }

        private static bool ContainsRidgeFootprint(
            Rect bounds,
            MountainRoadRidgeDescriptor ridge)
        {
            Vector3 halfSize = ridge.Size * 0.5f;
            float yaw = ridge.YawDegrees * Mathf.Deg2Rad;
            float halfX = Mathf.Abs(Mathf.Cos(yaw)) * halfSize.x +
                          Mathf.Abs(Mathf.Sin(yaw)) * halfSize.z;
            float halfZ = Mathf.Abs(Mathf.Sin(yaw)) * halfSize.x +
                          Mathf.Abs(Mathf.Cos(yaw)) * halfSize.z;
            const float tolerance = 0.01f;
            return ridge.Center.x - halfX >= bounds.xMin - tolerance &&
                   ridge.Center.x + halfX <= bounds.xMax + tolerance &&
                   ridge.Center.z - halfZ >= bounds.yMin - tolerance &&
                   ridge.Center.z + halfZ <= bounds.yMax + tolerance;
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
