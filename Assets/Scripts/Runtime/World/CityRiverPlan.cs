using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityBridgeRole
    {
        Road = 0,
        ParkFootbridge = 1
    }

    public enum CityBridgeStyle
    {
        Works = 0,
        TimberPark = 1,
        Mouth = 2
    }

    public sealed class CityBridgeDefinition
    {
        public CityBridgeDefinition(
            string id,
            RoadEdge crossingEdge,
            CityBridgeRole role,
            CityBridgeStyle style,
            float deckWidth,
            Vector2Int interiorDirection)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A bridge requires a stable ID.",
                    nameof(id));
            }

            if (!crossingEdge.IsHorizontal)
            {
                throw new ArgumentException(
                    "A north-south river bridge must cross east-west.",
                    nameof(crossingEdge));
            }

            if (!Enum.IsDefined(typeof(CityBridgeRole), role) ||
                !Enum.IsDefined(typeof(CityBridgeStyle), style))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }

            if (!IsFinite(deckWidth) || deckWidth < 2f)
            {
                throw new ArgumentOutOfRangeException(nameof(deckWidth));
            }

            if (role == CityBridgeRole.Road &&
                deckWidth < CityGenerationSettings.DefaultRoadWidth)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deckWidth),
                    "Road bridges must preserve the Road v2 footprint.");
            }

            if (role == CityBridgeRole.Road &&
                interiorDirection != Vector2Int.up &&
                interiorDirection != Vector2Int.down)
            {
                throw new ArgumentException(
                    "Road bridges must point their landings into the city.",
                    nameof(interiorDirection));
            }

            if (role == CityBridgeRole.ParkFootbridge)
            {
                interiorDirection = Vector2Int.zero;
            }

            Id = id.Trim();
            CrossingEdge = crossingEdge;
            Role = role;
            Style = style;
            DeckWidth = deckWidth;
            InteriorDirection = interiorDirection;
        }

        public string Id { get; }
        public RoadEdge CrossingEdge { get; }
        public CityBridgeRole Role { get; }
        public CityBridgeStyle Style { get; }
        public float DeckWidth { get; }
        public Vector2Int InteriorDirection { get; }
        public bool CarriesRoadTraffic => Role == CityBridgeRole.Road;
        public bool HasLowerLandings => CarriesRoadTraffic;

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public sealed class CityRiverDefinition
    {
        public const float DefaultChannelWidth = 10f;
        public const float DefaultPromenadeWidth = 3f;
        public const float ParkFootbridgeWidth = 2.8f;

        private readonly ReadOnlyCollection<CityBridgeDefinition> bridges;
        private readonly Dictionary<RoadEdge, CityBridgeDefinition>
            bridgesByEdge;

        public CityRiverDefinition(
            string id,
            int corridorCellX,
            int coreMinimumZ,
            int coreMaximumZExclusive,
            IEnumerable<CityBridgeDefinition> sourceBridges,
            float channelWidth = DefaultChannelWidth,
            float promenadeWidth = DefaultPromenadeWidth)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A river requires a stable ID.",
                    nameof(id));
            }

            if (corridorCellX < 1 ||
                coreMinimumZ < 0 ||
                coreMaximumZExclusive <= coreMinimumZ)
            {
                throw new ArgumentOutOfRangeException(nameof(corridorCellX));
            }

            if (!IsFinite(channelWidth) || channelWidth <= 0f ||
                !IsFinite(promenadeWidth) || promenadeWidth < 2.2f)
            {
                throw new ArgumentOutOfRangeException(nameof(channelWidth));
            }

            if (sourceBridges == null)
            {
                throw new ArgumentNullException(nameof(sourceBridges));
            }

            Id = id.Trim();
            CorridorCellX = corridorCellX;
            CoreMinimumZ = coreMinimumZ;
            CoreMaximumZExclusive = coreMaximumZExclusive;
            ChannelWidth = channelWidth;
            PromenadeWidth = promenadeWidth;
            var copied = new List<CityBridgeDefinition>();
            bridgesByEdge =
                new Dictionary<RoadEdge, CityBridgeDefinition>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int roadCount = 0;
            int footbridgeCount = 0;
            foreach (CityBridgeDefinition bridge in sourceBridges)
            {
                if (bridge == null ||
                    !ids.Add(bridge.Id) ||
                    !bridgesByEdge.TryAdd(bridge.CrossingEdge, bridge) ||
                    bridge.CrossingEdge.A.x != CorridorCellX ||
                    bridge.CrossingEdge.B.x != CorridorCellX + 1 ||
                    bridge.CrossingEdge.A.y < CoreMinimumZ ||
                    bridge.CrossingEdge.A.y > CoreMaximumZExclusive)
                {
                    throw new ArgumentException(
                        "River bridges must be unique declared crossings.",
                        nameof(sourceBridges));
                }

                copied.Add(bridge);
                if (bridge.CarriesRoadTraffic)
                {
                    roadCount++;
                }
                else
                {
                    footbridgeCount++;
                }
            }

            copied.Sort((left, right) =>
                RoadEdge.Compare(left.CrossingEdge, right.CrossingEdge));
            if (roadCount != 2 || footbridgeCount != 1 || copied.Count != 3)
            {
                throw new ArgumentException(
                    "The river requires two road bridges and one footbridge.",
                    nameof(sourceBridges));
            }

            bridges = new ReadOnlyCollection<CityBridgeDefinition>(copied);
        }

        public string Id { get; }
        public int CorridorCellX { get; }
        public int CoreMinimumZ { get; }
        public int CoreMaximumZExclusive { get; }
        public float ChannelWidth { get; }
        public float PromenadeWidth { get; }
        public IReadOnlyList<CityBridgeDefinition> Bridges => bridges;

        public bool TryGetBridge(
            RoadEdge edge,
            out CityBridgeDefinition bridge)
        {
            return bridgesByEdge.TryGetValue(edge, out bridge);
        }

        public bool CrossesCorridor(RoadEdge edge)
        {
            return edge.IsHorizontal &&
                   edge.A.x == CorridorCellX &&
                   edge.B.x == CorridorCellX + 1 &&
                   edge.A.y >= CoreMinimumZ &&
                   edge.A.y <= CoreMaximumZExclusive;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct CityRiverSegmentDescriptor
    {
        internal CityRiverSegmentDescriptor(
            Vector2Int cell,
            Rect waterBounds,
            float southWaterY,
            float northWaterY)
        {
            Cell = cell;
            WaterBounds = waterBounds;
            SouthWaterY = southWaterY;
            NorthWaterY = northWaterY;
        }

        public Vector2Int Cell { get; }
        public Rect WaterBounds { get; }
        public float SouthWaterY { get; }
        public float NorthWaterY { get; }
        public float AverageWaterY => (SouthWaterY + NorthWaterY) * 0.5f;
    }

    public readonly struct CityRiverPromenadeDescriptor
    {
        internal CityRiverPromenadeDescriptor(
            string id,
            bool westBank,
            Rect bounds,
            float southY,
            float northY)
        {
            Id = id ?? string.Empty;
            WestBank = westBank;
            Bounds = bounds;
            SouthY = southY;
            NorthY = northY;
        }

        public string Id { get; }
        public bool WestBank { get; }
        public Rect Bounds { get; }
        public float SouthY { get; }
        public float NorthY { get; }
    }

    public readonly struct CityRiverBridgeDescriptor
    {
        internal CityRiverBridgeDescriptor(
            CityBridgeDefinition definition,
            Rect deckBounds,
            Rect spanBounds,
            float westY,
            float eastY)
        {
            Definition = definition;
            DeckBounds = deckBounds;
            SpanBounds = spanBounds;
            WestY = westY;
            EastY = eastY;
        }

        public CityBridgeDefinition Definition { get; }
        public Rect DeckBounds { get; }
        public Rect SpanBounds { get; }
        public float WestY { get; }
        public float EastY { get; }
        public float AverageY => (WestY + EastY) * 0.5f;
    }

    public readonly struct CityRiverLandingDescriptor
    {
        internal CityRiverLandingDescriptor(
            string id,
            string bridgeId,
            bool westBank,
            Rect stairBounds,
            Rect platformBounds,
            float upperY,
            float lowerY,
            int stepCount,
            Vector3 descentDirection)
        {
            Id = id ?? string.Empty;
            BridgeId = bridgeId ?? string.Empty;
            WestBank = westBank;
            StairBounds = stairBounds;
            PlatformBounds = platformBounds;
            UpperY = upperY;
            LowerY = lowerY;
            StepCount = stepCount;
            DescentDirection = descentDirection;
        }

        public string Id { get; }
        public string BridgeId { get; }
        public bool WestBank { get; }
        public Rect StairBounds { get; }
        public Rect PlatformBounds { get; }
        public float UpperY { get; }
        public float LowerY { get; }
        public int StepCount { get; }
        public Vector3 DescentDirection { get; }
    }

    public sealed class CityRiverPlan
    {
        private readonly Dictionary<RoadEdge, CityRiverBridgeDescriptor>
            bridgesByEdge;

        internal CityRiverPlan(
            CityRiverDefinition definition,
            IList<CityRiverSegmentDescriptor> segments,
            IList<CityRiverPromenadeDescriptor> promenades,
            IList<CityRiverBridgeDescriptor> bridges,
            IList<CityRiverLandingDescriptor> landings)
        {
            Definition = definition;
            Segments = new ReadOnlyCollection<CityRiverSegmentDescriptor>(
                new List<CityRiverSegmentDescriptor>(segments));
            Promenades =
                new ReadOnlyCollection<CityRiverPromenadeDescriptor>(
                    new List<CityRiverPromenadeDescriptor>(promenades));
            Bridges = new ReadOnlyCollection<CityRiverBridgeDescriptor>(
                new List<CityRiverBridgeDescriptor>(bridges));
            Landings = new ReadOnlyCollection<CityRiverLandingDescriptor>(
                new List<CityRiverLandingDescriptor>(landings));
            bridgesByEdge =
                new Dictionary<RoadEdge, CityRiverBridgeDescriptor>();
            for (int index = 0; index < Bridges.Count; index++)
            {
                bridgesByEdge.Add(
                    Bridges[index].Definition.CrossingEdge,
                    Bridges[index]);
            }
        }

        public CityRiverDefinition Definition { get; }
        public IReadOnlyList<CityRiverSegmentDescriptor> Segments { get; }
        public IReadOnlyList<CityRiverPromenadeDescriptor> Promenades { get; }
        public IReadOnlyList<CityRiverBridgeDescriptor> Bridges { get; }
        public IReadOnlyList<CityRiverLandingDescriptor> Landings { get; }
        public bool IsEnabled => Definition != null;

        public bool TryGetBridge(
            RoadEdge edge,
            out CityRiverBridgeDescriptor bridge)
        {
            return bridgesByEdge.TryGetValue(edge, out bridge);
        }

        public bool ContainsBridgeDeck(Vector3 position)
        {
            for (int index = 0; index < Bridges.Count; index++)
            {
                if (Contains(Bridges[index].DeckBounds, position))
                {
                    return true;
                }
            }

            return false;
        }

        public bool ContainsWater(Vector3 position)
        {
            if (ContainsBridgeDeck(position))
            {
                return false;
            }

            for (int index = 0; index < Segments.Count; index++)
            {
                if (Contains(Segments[index].WaterBounds, position))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(Rect bounds, Vector3 position)
        {
            return position.x >= bounds.xMin &&
                   position.x <= bounds.xMax &&
                   position.z >= bounds.yMin &&
                   position.z <= bounds.yMax;
        }
    }

    public static class CityRiverPlanner
    {
        public const float QuayEdgeOffset = 0.48f;

        private const float SouthWaterY = 2.4f;
        private const float SeaWaterY = 0f;
        private const float LowerPlatformOffset = 0.4f;
        private const float LandingWidth = 3f;
        private const float LandingDepth = 5f;
        private const float StairRun = 3.2f;
        private const float LandingApproachRun = 3f;

        public static CityRiverPlan Create(
            CityBlueprint blueprint,
            CityGenerationSettings settings,
            Vector3 origin,
            CityElevationPlan elevationPlan)
        {
            if (blueprint?.River == null)
            {
                return new CityRiverPlan(
                    null,
                    Array.Empty<CityRiverSegmentDescriptor>(),
                    Array.Empty<CityRiverPromenadeDescriptor>(),
                    Array.Empty<CityRiverBridgeDescriptor>(),
                    Array.Empty<CityRiverLandingDescriptor>());
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (elevationPlan == null)
            {
                throw new ArgumentNullException(nameof(elevationPlan));
            }

            CityRiverDefinition definition = blueprint.River;
            float nodeWidth = settings.NodeSpacing.x;
            float nodeDepth = settings.NodeSpacing.y;
            float westNodeX = origin.x + definition.CorridorCellX * nodeWidth;
            float eastNodeX = westNodeX + nodeWidth;
            float centerX = (westNodeX + eastNodeX) * 0.5f;
            float channelHalf = definition.ChannelWidth * 0.5f;
            float channelXMin = centerX - channelHalf;
            float channelXMax = centerX + channelHalf;
            float roadHalf = settings.RoadWidth * 0.5f;
            float corridorSouth = origin.z +
                                  definition.CoreMinimumZ * nodeDepth;
            float corridorNorth = origin.z +
                                  definition.CoreMaximumZExclusive *
                                  nodeDepth;
            var segments = new List<CityRiverSegmentDescriptor>();
            for (int z = definition.CoreMinimumZ;
                 z <= definition.CoreMaximumZExclusive;
                 z++)
            {
                float southZ = origin.z + z * nodeDepth;
                float northZ = southZ + nodeDepth;
                segments.Add(new CityRiverSegmentDescriptor(
                    new Vector2Int(definition.CorridorCellX, z),
                    Rect.MinMaxRect(
                        channelXMin,
                        southZ,
                        channelXMax,
                        northZ),
                    ResolveWaterY(definition, z),
                    ResolveWaterY(definition, z + 1)));
            }

            var promenades = new List<CityRiverPromenadeDescriptor>
            {
                new CityRiverPromenadeDescriptor(
                    "river-promenade-west",
                    true,
                    Rect.MinMaxRect(
                        westNodeX + roadHalf,
                        corridorSouth,
                        westNodeX + roadHalf + definition.PromenadeWidth,
                        corridorNorth),
                    elevationPlan.GetNodeElevation(new Vector2Int(
                        definition.CorridorCellX,
                        definition.CoreMinimumZ)),
                    elevationPlan.GetNodeElevation(new Vector2Int(
                        definition.CorridorCellX,
                        definition.CoreMaximumZExclusive))),
                new CityRiverPromenadeDescriptor(
                    "river-promenade-east",
                    false,
                    Rect.MinMaxRect(
                        eastNodeX - roadHalf - definition.PromenadeWidth,
                        corridorSouth,
                        eastNodeX - roadHalf,
                        corridorNorth),
                    elevationPlan.GetNodeElevation(new Vector2Int(
                        definition.CorridorCellX + 1,
                        definition.CoreMinimumZ)),
                    elevationPlan.GetNodeElevation(new Vector2Int(
                        definition.CorridorCellX + 1,
                        definition.CoreMaximumZExclusive)))
            };

            var bridges = new List<CityRiverBridgeDescriptor>();
            var landings = new List<CityRiverLandingDescriptor>();
            for (int index = 0; index < definition.Bridges.Count; index++)
            {
                CityBridgeDefinition bridge = definition.Bridges[index];
                int z = bridge.CrossingEdge.A.y;
                float centerZ = origin.z + z * nodeDepth;
                float halfWidth = bridge.DeckWidth * 0.5f;
                float westY = elevationPlan.GetNodeElevation(
                    bridge.CrossingEdge.A);
                float eastY = elevationPlan.GetNodeElevation(
                    bridge.CrossingEdge.B);
                bridges.Add(new CityRiverBridgeDescriptor(
                    bridge,
                    Rect.MinMaxRect(
                        westNodeX,
                        centerZ - halfWidth,
                        eastNodeX,
                        centerZ + halfWidth),
                    Rect.MinMaxRect(
                        channelXMin - QuayEdgeOffset,
                        centerZ - halfWidth,
                        channelXMax + QuayEdgeOffset,
                        centerZ + halfWidth),
                    westY,
                    eastY));

                if (!bridge.HasLowerLandings)
                {
                    continue;
                }

                float direction = bridge.InteriorDirection.y;
                float deckEdgeZ = centerZ + direction * halfWidth;
                float stairCenterZ = deckEdgeZ +
                    direction * (LandingApproachRun + StairRun * 0.5f);
                float platformCenterZ = deckEdgeZ +
                    direction * (
                        LandingApproachRun + StairRun +
                        LandingDepth * 0.5f);
                float waterY = ResolveWaterY(definition, z);
                AddLanding(
                    landings,
                    bridge,
                    true,
                    channelXMin - LandingWidth * 0.5f,
                    stairCenterZ,
                    platformCenterZ,
                    westY + CityStreetSurfacePlanner.SidewalkTop,
                    waterY + LowerPlatformOffset,
                    direction);
                AddLanding(
                    landings,
                    bridge,
                    false,
                    channelXMax + LandingWidth * 0.5f,
                    stairCenterZ,
                    platformCenterZ,
                    eastY + CityStreetSurfacePlanner.SidewalkTop,
                    waterY + LowerPlatformOffset,
                    direction);
            }

            return new CityRiverPlan(
                definition,
                segments,
                promenades,
                bridges,
                landings);
        }

        public static float ResolveWaterY(
            CityRiverDefinition definition,
            int nodeZ)
        {
            float amount = Mathf.InverseLerp(
                definition.CoreMinimumZ,
                definition.CoreMaximumZExclusive + 1,
                nodeZ);
            return Mathf.Lerp(SouthWaterY, SeaWaterY, amount);
        }

        private static void AddLanding(
            ICollection<CityRiverLandingDescriptor> target,
            CityBridgeDefinition bridge,
            bool westBank,
            float centerX,
            float stairCenterZ,
            float platformCenterZ,
            float upperY,
            float lowerY,
            float direction)
        {
            float drop = Mathf.Max(0.01f, upperY - lowerY);
            int stepCount = Mathf.Clamp(
                Mathf.CeilToInt(drop / 0.16f),
                8,
                10);
            target.Add(new CityRiverLandingDescriptor(
                $"{bridge.Id}-{(westBank ? "west" : "east")}-landing",
                bridge.Id,
                westBank,
                new Rect(
                    centerX - LandingWidth * 0.5f,
                    stairCenterZ - StairRun * 0.5f,
                    LandingWidth,
                    StairRun),
                new Rect(
                    centerX - LandingWidth * 0.5f,
                    platformCenterZ - LandingDepth * 0.5f,
                    LandingWidth,
                    LandingDepth),
                upperY,
                lowerY,
                stepCount,
                new Vector3(0f, 0f, direction)));
        }
    }
}
