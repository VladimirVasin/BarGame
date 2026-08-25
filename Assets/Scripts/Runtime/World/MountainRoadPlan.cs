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
        SnowPole = 4
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

    public sealed class MountainRoadPlateauDescriptor
    {
        private readonly ReadOnlyCollection<Vector2> verticesXZ;

        internal MountainRoadPlateauDescriptor(
            Vector3 center,
            Vector3 forward,
            float entryDistance,
            IList<Vector2> sourceVertices)
        {
            Center = center;
            Forward = forward.normalized;
            Right = Vector3.Cross(Vector3.up, Forward).normalized;
            EntryDistance = entryDistance;
            verticesXZ = new ReadOnlyCollection<Vector2>(
                new List<Vector2>(sourceVertices));
            BoundsXZ = CalculateBounds(verticesXZ);
        }

        public Vector3 Center { get; }
        public Vector3 Forward { get; }
        public Vector3 Right { get; }
        public float EntryDistance { get; }
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
            IList<MountainRoadSoundAnchor> sourceSoundAnchors)
        {
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
