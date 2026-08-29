using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum MountainRoadRouteSection
    {
        LowerClimb = 0,
        Hairpin = 1,
        BridgeApproach = 2,
        Bridge = 3,
        UpperClimb = 4,
        UpperApproach = 5
    }

    public enum MountainRoadForestLayer
    {
        Physical = 0,
        Mid = 1,
        Far = 2
    }

    public enum MountainRoadMiscKind
    {
        Boulder = 0,
        FallenLog = 1,
        Stump = 2,
        DeadTree = 3,
        GuardRail = 4,
        Culvert = 5,
        ConvexMirror = 6,
        UtilityCabinet = 7,
        UtilityCable = 8,
        SnowPole = 9,
        AbandonedChair = 10,
        TunnelLamp = 11
    }

    public enum MountainRoadSoundAnchorKind
    {
        TunnelLampBallast = 0,
        CulvertWater = 1,
        LooseGuardRail = 2,
        UtilityCable = 3,
        SnowPole = 4,

        /// <summary>The halyard slapping the windsock's mast.</summary>
        WindsockHalyard = 5,

        /// <summary>A tarp roped over freight nobody is coming for.</summary>
        LoadTarp = 6
    }

    public enum MountainRoadRidgeLayer
    {
        Mid = 0,
        FarSnow = 1
    }

    public readonly struct MountainRoadRouteSample
    {
        internal MountainRoadRouteSample(
            string stableId,
            float distance,
            Vector3 position,
            Vector3 forward,
            float width,
            MountainRoadRouteSection section,
            int hairpinIndex)
        {
            StableId = stableId ?? string.Empty;
            Distance = distance;
            Position = position;
            Forward = forward;
            Width = width;
            Section = section;
            HairpinIndex = hairpinIndex;
        }

        public string StableId { get; }
        public float Distance { get; }
        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public Vector3 Right => Vector3.Cross(Vector3.up, Forward).normalized;
        public float Width { get; }
        public MountainRoadRouteSection Section { get; }
        public int HairpinIndex { get; }
        public bool IsHairpin => HairpinIndex >= 0;
        public bool IsBridge => Section == MountainRoadRouteSection.Bridge;
    }

    public readonly struct MountainRoadHairpinDescriptor
    {
        internal MountainRoadHairpinDescriptor(
            string stableId,
            int index,
            float startDistance,
            float endDistance,
            Vector2 centerXZ,
            Vector3 apexPosition,
            int turnSide)
        {
            StableId = stableId ?? string.Empty;
            Index = index;
            StartDistance = startDistance;
            EndDistance = endDistance;
            CenterXZ = centerXZ;
            ApexPosition = apexPosition;
            TurnSide = turnSide;
        }

        public string StableId { get; }
        public int Index { get; }
        public float StartDistance { get; }
        public float EndDistance { get; }
        public Vector2 CenterXZ { get; }
        public Vector3 ApexPosition { get; }
        public int TurnSide { get; }
    }

    public sealed class MountainRoadBridgeDescriptor
    {
        internal MountainRoadBridgeDescriptor(
            string stableId,
            float startDistance,
            float endDistance,
            Vector3 start,
            Vector3 end,
            float clearWidth,
            float deckWidth,
            float deckThickness,
            float railHeight,
            float gorgeFloorY,
            float gorgeHalfWidth,
            float abutmentBlendLength)
        {
            StableId = stableId ?? string.Empty;
            StartDistance = startDistance;
            EndDistance = endDistance;
            Start = start;
            End = end;
            ClearWidth = clearWidth;
            DeckWidth = deckWidth;
            DeckThickness = deckThickness;
            RailHeight = railHeight;
            GorgeFloorY = gorgeFloorY;
            GorgeHalfWidth = gorgeHalfWidth;
            AbutmentBlendLength = abutmentBlendLength;

            Vector3 delta = end - start;
            Vector3 planar = new Vector3(delta.x, 0f, delta.z);
            Forward = planar.normalized;
            Right = Vector3.Cross(Vector3.up, Forward).normalized;
            Center = (start + end) * 0.5f;
        }

        public string StableId { get; }
        public float StartDistance { get; }
        public float EndDistance { get; }
        public float Length => EndDistance - StartDistance;
        public Vector3 Start { get; }
        public Vector3 End { get; }
        public Vector3 Center { get; }
        public Vector3 Forward { get; }
        public Vector3 Right { get; }
        public float ClearWidth { get; }
        public float DeckWidth { get; }
        public float DeckThickness { get; }
        public float RailHeight { get; }
        public float GorgeFloorY { get; }
        public float GorgeHalfWidth { get; }
        public float AbutmentBlendLength { get; }
    }

    public sealed class MountainRoadRoutePlan
    {
        private readonly ReadOnlyCollection<MountainRoadRouteSample> samples;
        private readonly ReadOnlyCollection<MountainRoadHairpinDescriptor>
            hairpins;

        internal MountainRoadRoutePlan(
            IList<MountainRoadRouteSample> sourceSamples,
            float length,
            IList<MountainRoadHairpinDescriptor> sourceHairpins,
            MountainRoadBridgeDescriptor bridge)
        {
            samples = new ReadOnlyCollection<MountainRoadRouteSample>(
                new List<MountainRoadRouteSample>(sourceSamples));
            hairpins = new ReadOnlyCollection<MountainRoadHairpinDescriptor>(
                new List<MountainRoadHairpinDescriptor>(sourceHairpins));
            Length = length;
            Bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }

        public IReadOnlyList<MountainRoadRouteSample> Samples => samples;
        public IReadOnlyList<MountainRoadHairpinDescriptor> Hairpins => hairpins;
        public MountainRoadBridgeDescriptor Bridge { get; }
        public float Length { get; }
        public Vector3 Start => samples[0].Position;
        public Vector3 End => samples[samples.Count - 1].Position;
        public float ElevationGain => End.y - Start.y;

        public MountainRoadRouteSample Sample(float distance)
        {
            float clamped = Mathf.Clamp(distance, 0f, Length);
            int low = 0;
            int high = samples.Count - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (samples[middle].Distance <= clamped)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            MountainRoadRouteSample first = samples[low];
            if (low == samples.Count - 1 ||
                Mathf.Abs(first.Distance - clamped) <= 0.0001f)
            {
                return first;
            }

            MountainRoadRouteSample second = samples[low + 1];
            float t = Mathf.InverseLerp(
                first.Distance,
                second.Distance,
                clamped);
            Vector3 forward = Vector3.Slerp(
                first.Forward,
                second.Forward,
                t).normalized;
            int hairpin = first.HairpinIndex == second.HairpinIndex
                ? first.HairpinIndex
                : (t < 0.5f
                    ? first.HairpinIndex
                    : second.HairpinIndex);
            MountainRoadRouteSection section = t < 0.5f
                ? first.Section
                : second.Section;
            return new MountainRoadRouteSample(
                $"route-sample-{clamped:000.000}",
                clamped,
                Vector3.Lerp(first.Position, second.Position, t),
                forward,
                Mathf.Lerp(first.Width, second.Width, t),
                section,
                hairpin);
        }
    }

    public sealed class MountainRoadTunnelDescriptor
    {
        internal MountainRoadTunnelDescriptor(
            Vector3 portalGroundCenter,
            Vector3 outwardAxis,
            float openingWidth,
            float openingHeight,
            float visualDepth,
            Vector3 spawnPosition)
        {
            PortalGroundCenter = portalGroundCenter;
            OutwardAxis = outwardAxis;
            OpeningWidth = openingWidth;
            OpeningHeight = openingHeight;
            VisualDepth = visualDepth;
            SpawnPosition = spawnPosition;
        }

        public Vector3 PortalGroundCenter { get; }
        public Vector3 OutwardAxis { get; }
        public float OpeningWidth { get; }
        public float OpeningHeight { get; }
        public float VisualDepth { get; }
        public Vector3 SpawnPosition { get; }
        public Vector3 SpawnForward => OutwardAxis;
    }

    /// <summary>
    /// A horizontal wedge on the ground, in world XZ. It says where the
    /// mountain is allowed to be absent: the terrain is cut away inside it
    /// and nothing that has to stay grounded may stand in it.
    ///
    /// The taper exists because a hard-walled cut is a razor slot. The last
    /// few degrees fall off gradually, which gives the opening real side
    /// walls; clearance work therefore measures against the OUTER angle,
    /// not the one the view is composed on.
    /// </summary>
    public readonly struct MountainRoadViewCorridor
    {
        internal MountainRoadViewCorridor(
            Vector3 apex,
            Vector3 axis,
            float halfAngleDegrees,
            float taperDegrees,
            float innerRadius,
            float outerRadius)
        {
            Apex = apex;
            Vector3 flat = axis;
            flat.y = 0f;
            Axis = flat.normalized;
            HalfAngleDegrees = halfAngleDegrees;
            TaperDegrees = taperDegrees;
            InnerRadius = innerRadius;
            OuterRadius = outerRadius;
        }

        public Vector3 Apex { get; }
        public Vector3 Axis { get; }
        public float HalfAngleDegrees { get; }
        public float TaperDegrees { get; }
        public float InnerRadius { get; }
        public float OuterRadius { get; }

        public float OuterHalfAngleDegrees =>
            HalfAngleDegrees + TaperDegrees;

        /// <summary>
        /// Signed distance to the outer wedge: positive inside, and outside
        /// the true metres to the nearest point of it, so a clearance rule
        /// reads <c>DepthInside(p) &lt;= -margin</c>.
        ///
        /// The angular and radial cases have to be answered together. A
        /// point that is both well off the axis AND past the far arc is
        /// nowhere near the cut, and treating its radial shortfall as a
        /// clearance reports a road a hundred and forty metres away as
        /// standing ten metres from the edge.
        /// </summary>
        public float DepthInside(Vector2 pointXZ)
        {
            var delta = new Vector2(
                pointXZ.x - Apex.x,
                pointXZ.y - Apex.z);
            float distance = delta.magnitude;
            if (distance <= 0.0001f)
            {
                return 0f;
            }

            var axis = new Vector2(Axis.x, Axis.z);
            float angle = Vector2.Angle(axis, delta / distance);
            float offWall = angle - OuterHalfAngleDegrees;
            if (offWall <= 0f)
            {
                // Angularly inside: either within the arc, or straight
                // past its far end.
                return distance <= OuterRadius
                    ? Mathf.Min(
                        OuterRadius - distance,
                        distance * Mathf.Sin(-offWall * Mathf.Deg2Rad))
                    : OuterRadius - distance;
            }

            // Angularly outside: measure to the nearer wall segment, which
            // runs from the apex to its own far endpoint.
            float radians = offWall * Mathf.Deg2Rad;
            float along = distance * Mathf.Cos(radians);
            if (along <= 0f)
            {
                return -distance;
            }

            if (along >= OuterRadius)
            {
                float squared = distance * distance +
                                OuterRadius * OuterRadius -
                                2f * distance * OuterRadius *
                                Mathf.Cos(radians);
                return -Mathf.Sqrt(Mathf.Max(0f, squared));
            }

            return -distance * Mathf.Sin(radians);
        }

        /// <summary>
        /// How much of the cut applies at this point: one inside the
        /// composed wedge past the lead-in, easing to zero across the
        /// taper and across <paramref name="innerBlend"/> metres of radius.
        /// </summary>
        public float Weight(Vector2 pointXZ, float innerBlend)
        {
            var delta = new Vector2(
                pointXZ.x - Apex.x,
                pointXZ.y - Apex.z);
            float distance = delta.magnitude;
            if (distance <= 0.0001f || distance > OuterRadius)
            {
                return 0f;
            }

            var axis = new Vector2(Axis.x, Axis.z);
            float angle = Vector2.Angle(axis, delta / distance);
            float lateral = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    HalfAngleDegrees,
                    OuterHalfAngleDegrees,
                    angle));
            float longitudinal = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    InnerRadius,
                    InnerRadius + innerBlend,
                    distance));
            return lateral * longitudinal;
        }
    }

    /// <summary>
    /// Where the terminal terrace stops being ground.
    ///
    /// This hangs off the plateau rather than off the terminal on purpose:
    /// <see cref="MountainRoadTerminalPlanner"/> already samples terrain
    /// height to ground the cableway, so anything that CHANGES terrain has
    /// to exist before the terminal does. The plateau descriptor is also
    /// already threaded into every terrain sample in the area, so putting
    /// it here costs no signature anywhere.
    /// </summary>
    public sealed class MountainRoadBrinkDescriptor
    {
        internal MountainRoadBrinkDescriptor(
            Vector3 rimStart,
            Vector3 rimEnd,
            Vector3 outward,
            float dropDepth,
            float edgeBlendDistance,
            MountainRoadViewCorridor corridor)
        {
            RimStart = rimStart;
            RimEnd = rimEnd;
            Vector3 flat = outward;
            flat.y = 0f;
            Outward = flat.normalized;
            DropDepth = dropDepth;
            EdgeBlendDistance = edgeBlendDistance;
            Corridor = corridor;
        }

        public Vector3 RimStart { get; }
        public Vector3 RimEnd { get; }
        public Vector3 Outward { get; }

        /// <summary>
        /// How far the ground is taken down inside the cut. It is a
        /// CONSTANT subtraction rather than a flat floor, so the cut bed
        /// keeps the macro slope and the far edge of the mesh is a
        /// uniformly lowered continuation instead of a second cliff.
        /// </summary>
        public float DropDepth { get; }

        public float EdgeBlendDistance { get; }
        public MountainRoadViewCorridor Corridor { get; }

        public Vector3 RimCenter => (RimStart + RimEnd) * 0.5f;
    }

    public sealed class MountainRoadPlateauDescriptor
    {
        private readonly ReadOnlyCollection<Vector2> verticesXZ;

        internal MountainRoadPlateauDescriptor(
            Vector3 center,
            Vector3 forward,
            float entryDistance,
            IList<Vector2> sourceVertices,
            MountainRoadBrinkDescriptor brink)
        {
            Center = center;
            Forward = forward.normalized;
            Right = Vector3.Cross(Vector3.up, Forward).normalized;
            EntryDistance = entryDistance;
            verticesXZ = new ReadOnlyCollection<Vector2>(
                new List<Vector2>(sourceVertices));
            BoundsXZ = CalculateBounds(verticesXZ);
            Brink = brink;
        }

        public Vector3 Center { get; }
        public Vector3 Forward { get; }
        public Vector3 Right { get; }
        public float EntryDistance { get; }

        /// <summary>The cut rim, or null on a plateau without one.</summary>
        public MountainRoadBrinkDescriptor Brink { get; }
        public IReadOnlyList<Vector2> VerticesXZ => verticesXZ;
        public Rect BoundsXZ { get; }
        public Vector2 Size => BoundsXZ.size;

        public bool Contains(Vector2 point)
        {
            bool inside = false;
            for (int first = 0, second = verticesXZ.Count - 1;
                 first < verticesXZ.Count;
                 second = first++)
            {
                Vector2 a = verticesXZ[first];
                Vector2 b = verticesXZ[second];
                bool crosses = (a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) *
                    (point.y - a.y) /
                    ((b.y - a.y) + Mathf.Epsilon) + a.x;
                if (crosses)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static Rect CalculateBounds(IReadOnlyList<Vector2> points)
        {
            float xMin = points[0].x;
            float xMax = xMin;
            float zMin = points[0].y;
            float zMax = zMin;
            for (int index = 1; index < points.Count; index++)
            {
                xMin = Mathf.Min(xMin, points[index].x);
                xMax = Mathf.Max(xMax, points[index].x);
                zMin = Mathf.Min(zMin, points[index].y);
                zMax = Mathf.Max(zMax, points[index].y);
            }

            return Rect.MinMaxRect(xMin, zMin, xMax, zMax);
        }
    }

    public readonly struct MountainRoadForestDescriptor
    {
        internal MountainRoadForestDescriptor(
            string stableId,
            MountainRoadForestLayer layer,
            Vector3 position,
            float height,
            float crownRadius,
            float yawDegrees,
            int paletteIndex,
            bool blocksMovement)
        {
            StableId = stableId ?? string.Empty;
            Layer = layer;
            Position = position;
            Height = height;
            CrownRadius = crownRadius;
            YawDegrees = yawDegrees;
            PaletteIndex = paletteIndex;
            BlocksMovement = blocksMovement;
        }

        public string StableId { get; }
        public MountainRoadForestLayer Layer { get; }
        public Vector3 Position { get; }
        public float Height { get; }
        public float CrownRadius { get; }
        public float YawDegrees { get; }
        public int PaletteIndex { get; }
        public bool BlocksMovement { get; }
        public float TrunkRadius => Mathf.Clamp(CrownRadius * 0.16f, 0.18f, 0.46f);
    }

    public readonly struct MountainRoadMiscDescriptor
    {
        internal MountainRoadMiscDescriptor(
            string stableId,
            MountainRoadMiscKind kind,
            Vector3 position,
            Quaternion rotation,
            Vector3 size,
            bool blocksMovement)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            Position = position;
            Rotation = rotation;
            Size = size;
            BlocksMovement = blocksMovement;
        }

        public string StableId { get; }
        public MountainRoadMiscKind Kind { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Size { get; }
        public bool BlocksMovement { get; }
        public Bounds WorldBounds => new Bounds(Position, Size);
    }

    public readonly struct MountainRoadSoundAnchor
    {
        internal MountainRoadSoundAnchor(
            string stableId,
            MountainRoadSoundAnchorKind kind,
            string sourceObjectStableId,
            Vector3 position,
            float audibleRadius)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            SourceObjectStableId = sourceObjectStableId ?? string.Empty;
            Position = position;
            AudibleRadius = audibleRadius;
        }

        public string StableId { get; }
        public MountainRoadSoundAnchorKind Kind { get; }
        public string SourceObjectStableId { get; }
        public Vector3 Position { get; }
        public float AudibleRadius { get; }
    }

    public readonly struct MountainRoadRidgeDescriptor
    {
        internal MountainRoadRidgeDescriptor(
            string stableId,
            MountainRoadRidgeLayer layer,
            Vector3 center,
            Vector3 size,
            float yawDegrees,
            int seed)
        {
            StableId = stableId ?? string.Empty;
            Layer = layer;
            Center = center;
            Size = size;
            YawDegrees = yawDegrees;
            Seed = seed;
        }

        public string StableId { get; }
        public MountainRoadRidgeLayer Layer { get; }
        public Vector3 Center { get; }
        public Vector3 Size { get; }
        public float YawDegrees { get; }
        public int Seed { get; }
    }

    internal static class MountainRoadRidgeGeometry
    {
        internal static float DistanceToFootprint(
            Vector2 point,
            MountainRoadRidgeDescriptor ridge)
        {
            Vector3 worldOffset = new Vector3(
                point.x - ridge.Center.x,
                0f,
                point.y - ridge.Center.z);
            Vector3 localOffset =
                Quaternion.Euler(0f, -ridge.YawDegrees, 0f) * worldOffset;
            float outsideX = Mathf.Max(
                0f,
                Mathf.Abs(localOffset.x) - ridge.Size.x * 0.5f);
            float outsideZ = Mathf.Max(
                0f,
                Mathf.Abs(localOffset.z) - ridge.Size.z * 0.5f);
            return Mathf.Sqrt(outsideX * outsideX + outsideZ * outsideZ);
        }

        /// <summary>
        /// How many stations the built crest is drawn from. A ridge is a
        /// sine, but a POLYGONAL one, and the difference is the whole reason
        /// this lives here: at its middle the drawn crest sits about `14%`
        /// below the box it is authored in, so a check against the box top
        /// passes on rock that was never built.
        /// </summary>
        internal const int RidgeCrestStations = 6;

        /// <summary>
        /// The crest the scenery factory actually builds, at width fraction
        /// <paramref name="amount"/>, in world Y.
        ///
        /// One shape, two readers - the mesh and the validator - and neither
        /// may hold a number the other does not. The cableway's occluder was
        /// validated against the top of its bounding box while the rock the
        /// player sees is this line, and the two are metres apart.
        /// </summary>
        internal static float CrestWorldY(
            MountainRoadRidgeDescriptor ridge,
            float amount)
        {
            float scaled = Mathf.Clamp01(amount) * (RidgeCrestStations - 1);
            int lower = Mathf.Clamp(
                Mathf.FloorToInt(scaled),
                0,
                RidgeCrestStations - 2);
            return ridge.Center.y + Mathf.Lerp(
                StationCrest(ridge, lower),
                StationCrest(ridge, lower + 1),
                scaled - lower);
        }

        /// <summary>The width fraction at which a world point crosses the
        /// ridge, and whether it is inside the ridge's own footprint at all.
        /// </summary>
        internal static bool TryGetCrossing(
            MountainRoadRidgeDescriptor ridge,
            Vector3 world,
            out float amount)
        {
            Vector3 local = Quaternion.Euler(0f, -ridge.YawDegrees, 0f) *
                            (world - ridge.Center);
            amount = local.x / ridge.Size.x + 0.5f;
            return Mathf.Abs(local.x) <= ridge.Size.x * 0.5f &&
                   Mathf.Abs(local.z) <= ridge.Size.z * 0.5f;
        }

        internal static float StationCrest(
            MountainRoadRidgeDescriptor ridge,
            int station)
        {
            return ridge.Size.y *
                   (StationFactor(ridge.Seed, station) - 0.5f);
        }

        /// <summary>
        /// The crest as a FRACTION of the ridge's authored height, which is
        /// what a planner needs before it has one: `crestWorldY = base +
        /// Size.y * factor`. The variation is a seeded eighth, so a ridge
        /// sized against its own bounding box clears the cable for seven
        /// seeds and buries the rule on the eighth - solve for this instead
        /// and the rock is tall enough by construction.
        /// </summary>
        internal static float CrestFactor(int ridgeSeed, float amount)
        {
            float scaled = Mathf.Clamp01(amount) * (RidgeCrestStations - 1);
            int lower = Mathf.Clamp(
                Mathf.FloorToInt(scaled),
                0,
                RidgeCrestStations - 2);
            return Mathf.Lerp(
                StationFactor(ridgeSeed, lower),
                StationFactor(ridgeSeed, lower + 1),
                scaled - lower);
        }

        private static float StationFactor(int ridgeSeed, int station)
        {
            float amount = station / (float)(RidgeCrestStations - 1);
            float edge = Mathf.Sin(amount * Mathf.PI);
            float variation = 0.84f +
                              ((ridgeSeed + station * 37) & 7) * 0.025f;
            return edge * variation;
        }
    }

    public sealed class MountainRoadPlan
    {
        private readonly ReadOnlyCollection<MountainRoadForestDescriptor> forest;
        private readonly ReadOnlyCollection<MountainRoadMiscDescriptor> misc;
        private readonly ReadOnlyCollection<MountainRoadRidgeDescriptor> ridges;
        private readonly ReadOnlyCollection<MountainRoadSoundAnchor> soundAnchors;

        internal MountainRoadPlan(
            int seed,
            MountainRoadTunnelDescriptor tunnel,
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau,
            MountainRoadTerminalPlan terminal,
            Rect terrainBoundsXZ,
            Bounds worldBounds,
            IList<MountainRoadForestDescriptor> sourceForest,
            IList<MountainRoadMiscDescriptor> sourceMisc,
            IList<MountainRoadRidgeDescriptor> sourceRidges,
            IList<MountainRoadSoundAnchor> sourceSoundAnchors,
            MountainRoadVistaPlan vista)
        {
            Vista = vista;
            Seed = seed;
            Tunnel = tunnel ?? throw new ArgumentNullException(nameof(tunnel));
            Route = route ?? throw new ArgumentNullException(nameof(route));
            Plateau = plateau ?? throw new ArgumentNullException(nameof(plateau));
            Terminal = terminal ??
                throw new ArgumentNullException(nameof(terminal));
            TerrainBoundsXZ = terrainBoundsXZ;
            WorldBounds = worldBounds;
            forest = Copy(sourceForest);
            misc = Copy(sourceMisc);
            ridges = Copy(sourceRidges);
            soundAnchors = Copy(sourceSoundAnchors);
        }

        public int Seed { get; }
        public MountainRoadTunnelDescriptor Tunnel { get; }
        public MountainRoadRoutePlan Route { get; }
        public MountainRoadPlateauDescriptor Plateau { get; }
        public MountainRoadTerminalPlan Terminal { get; }

        /// <summary>What is seen over the brink, or null without one.</summary>
        public MountainRoadVistaPlan Vista { get; }

        public MountainRoadBridgeDescriptor Bridge => Route.Bridge;
        public Rect TerrainBoundsXZ { get; }
        public Bounds WorldBounds { get; }
        public Vector3 SpawnPosition => Tunnel.SpawnPosition;
        public Vector3 SpawnForward => Tunnel.SpawnForward;
        public IReadOnlyList<MountainRoadForestDescriptor> Forest => forest;
        public IReadOnlyList<MountainRoadMiscDescriptor> Misc => misc;
        public IReadOnlyList<MountainRoadRidgeDescriptor> Ridges => ridges;
        public IReadOnlyList<MountainRoadSoundAnchor> SoundAnchors => soundAnchors;

        private static ReadOnlyCollection<T> Copy<T>(IList<T> source)
        {
            return new ReadOnlyCollection<T>(new List<T>(source));
        }
    }
}
