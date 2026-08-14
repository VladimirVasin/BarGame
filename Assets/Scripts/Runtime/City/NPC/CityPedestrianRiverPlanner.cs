using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static partial class CityPedestrianPlanner
    {
        private static void BuildRiverPaths(
            CityLayout layout,
            IReadOnlyDictionary<Vector2Int, List<LaneEndpoint>>
                endpointsByNode,
            GraphBuilder graph)
        {
            CityRiverPlan river = layout.River;
            if (river == null || !river.IsEnabled)
            {
                return;
            }

            var footbridgeNodes = new Dictionary<string, int[]>(
                StringComparer.Ordinal);
            for (int bridgeIndex = 0;
                 bridgeIndex < river.Bridges.Count;
                 bridgeIndex++)
            {
                CityRiverBridgeDescriptor bridge =
                    river.Bridges[bridgeIndex];
                if (bridge.Definition.Role ==
                    CityBridgeRole.ParkFootbridge)
                {
                    footbridgeNodes.Add(
                        bridge.Definition.Id,
                        new[] { -1, -1 });
                }
            }

            for (int promenadeIndex = 0;
                 promenadeIndex < river.Promenades.Count;
                 promenadeIndex++)
            {
                CityRiverPromenadeDescriptor promenade =
                    river.Promenades[promenadeIndex];
                string bankId = promenade.WestBank ? "west" : "east";
                float laneInset = Mathf.Min(
                    promenade.Bounds.width * 0.5f,
                    AgentRadius + 0.1f);
                float centerX = promenade.WestBank
                    ? promenade.Bounds.xMin + laneInset
                    : promenade.Bounds.xMax - laneInset;
                var points = new List<RiverLanePoint>();
                AddRiverPromenadePoint(
                    promenade,
                    $"river:{bankId}:south",
                    promenade.Bounds.yMin + AgentRadius,
                    centerX,
                    null,
                    points,
                    graph);

                for (int bridgeIndex = 0;
                     bridgeIndex < river.Bridges.Count;
                     bridgeIndex++)
                {
                    CityRiverBridgeDescriptor bridge =
                        river.Bridges[bridgeIndex];
                    CityBridgeDefinition definition = bridge.Definition;
                    if (definition.Role ==
                        CityBridgeRole.ParkFootbridge)
                    {
                        float height = (promenade.WestBank
                            ? bridge.WestY
                            : bridge.EastY) +
                            CityStreetSurfacePlanner.RoadTop;
                        int node = AddRiverPromenadePoint(
                            promenade,
                            $"river:{bankId}:bridge:{definition.Id}",
                            bridge.DeckBounds.center.y,
                            centerX,
                            height,
                            points,
                            graph);
                        footbridgeNodes[definition.Id][
                            promenade.WestBank ? 0 : 1] = node;
                        continue;
                    }

                    Vector2Int gridNode = promenade.WestBank
                        ? definition.CrossingEdge.A
                        : definition.CrossingEdge.B;
                    List<LaneEndpoint> endpoints =
                        endpointsByNode[gridNode];
                    var bridgeEndpoints = new List<LaneEndpoint>(2);
                    for (int endpointIndex = 0;
                         endpointIndex < endpoints.Count;
                         endpointIndex++)
                    {
                        if (endpoints[endpointIndex].Edge ==
                            definition.CrossingEdge)
                        {
                            bridgeEndpoints.Add(endpoints[endpointIndex]);
                        }
                    }

                    bridgeEndpoints.Sort((left, right) =>
                        left.Position.z.CompareTo(right.Position.z));
                    if (bridgeEndpoints.Count != 2)
                    {
                        throw new InvalidOperationException(
                            $"River bridge '{definition.Id}' must expose " +
                            "two sidewalk endpoints per bank.");
                    }

                    for (int endpointIndex = 0;
                         endpointIndex < bridgeEndpoints.Count;
                         endpointIndex++)
                    {
                        LaneEndpoint endpoint =
                            bridgeEndpoints[endpointIndex];
                        string sideId = endpointIndex == 0
                            ? "south"
                            : "north";
                        int promenadeNode = AddRiverPromenadePoint(
                            promenade,
                            $"river:{bankId}:bridge:{definition.Id}:" +
                            sideId,
                            endpoint.Position.z,
                            centerX,
                            endpoint.Position.y,
                            points,
                            graph);
                        graph.AddLink(
                            $"river-access:{definition.Id}:{bankId}:" +
                            sideId,
                            endpoint.NodeIndex,
                            promenadeNode,
                            CityPedestrianLinkKind.Sidewalk,
                            false);
                    }
                }

                AddRiverPromenadePoint(
                    promenade,
                    $"river:{bankId}:north",
                    promenade.Bounds.yMax - AgentRadius,
                    centerX,
                    null,
                    points,
                    graph);
                points.Sort(RiverLanePoint.Compare);
                for (int pointIndex = 1;
                     pointIndex < points.Count;
                     pointIndex++)
                {
                    graph.AddLink(
                        $"river-promenade:{promenade.Id}:" +
                        $"{pointIndex - 1}",
                        points[pointIndex - 1].NodeIndex,
                        points[pointIndex].NodeIndex,
                        CityPedestrianLinkKind.Sidewalk,
                        false);
                }
            }

            foreach (KeyValuePair<string, int[]> pair in footbridgeNodes)
            {
                if (pair.Value[0] < 0 || pair.Value[1] < 0)
                {
                    throw new InvalidOperationException(
                        $"Park footbridge '{pair.Key}' has no promenade " +
                        "connection on both banks.");
                }

                graph.AddLink(
                    $"river-footbridge:{pair.Key}",
                    pair.Value[0],
                    pair.Value[1],
                    CityPedestrianLinkKind.Sidewalk,
                    false);
            }
        }

        private static int AddRiverPromenadePoint(
            CityRiverPromenadeDescriptor promenade,
            string id,
            float z,
            float x,
            float? fixedHeight,
            ICollection<RiverLanePoint> points,
            GraphBuilder graph)
        {
            z = Mathf.Clamp(
                z,
                promenade.Bounds.yMin,
                promenade.Bounds.yMax);
            float amount = Mathf.InverseLerp(
                promenade.Bounds.yMin,
                promenade.Bounds.yMax,
                z);
            float height = fixedHeight ??
                Mathf.Lerp(
                    promenade.SouthY,
                    promenade.NorthY,
                    amount);
            int node = graph.AddNode(
                id,
                new Vector3(x, height, z),
                false);
            points.Add(new RiverLanePoint(z, node));
            return node;
        }

        private readonly struct RiverLanePoint
        {
            public RiverLanePoint(float z, int nodeIndex)
            {
                Z = z;
                NodeIndex = nodeIndex;
            }

            public float Z { get; }
            public int NodeIndex { get; }

            public static int Compare(
                RiverLanePoint left,
                RiverLanePoint right)
            {
                int zComparison = left.Z.CompareTo(right.Z);
                return zComparison != 0
                    ? zComparison
                    : left.NodeIndex.CompareTo(right.NodeIndex);
            }
        }
    }
}
