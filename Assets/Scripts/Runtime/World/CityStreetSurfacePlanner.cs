using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityStreetSurfacePlanner
    {
        public const float SidewalkWidth = 1f;
        public const float RoadTop = 0.08f;
        public const float SidewalkTop = 0.14f;
        public const float CrosswalkDepth = 2.4f;
        public const float BusApproachApronLength = 4.5f;
        public const int CrosswalkStripeCount = 4;
        public const int MaximumCrosswalkIntersections =
            CityStreetIntersectionSelector.MaximumIntersectionCount;

        private const float RoadSurfaceHeight = RoadTop * 2f;
        private const float SidewalkHeight = SidewalkTop - RoadTop;
        private const float CenterDashWidth = 0.13f;
        private const float MaximumCenterDashLength = 2.1f;
        private const float MarkingHeight = 0.025f;
        private const float MarkingCenterAboveRoadBase = 0.095f;
        private const float CrosswalkStripeDepth = 0.36f;
        private const float GeometryTolerance = 0.0001f;

        public static CityStreetSurfacePlan Create(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            float carriagewayWidth =
                layout.RoadWidth - (SidewalkWidth * 2f);
            if (!IsFinite(layout.RoadWidth) ||
                carriagewayWidth <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(layout),
                    layout.RoadWidth,
                    "Road width must leave positive carriageway space " +
                    "between both sidewalks.");
            }

            var streetSurfaces = new List<Bounds>();
            var parkPaths = new List<Bounds>();
            var sidewalks = new List<Bounds>();
            var centerMarkings = new List<Bounds>();
            var crosswalkMarkings = new List<Bounds>();
            var streetGeometry = new List<RuntimeOrientedBox>();
            var parkPathGeometry = new List<RuntimeOrientedBox>();
            var sidewalkGeometry = new List<RuntimeOrientedBox>();
            var centerMarkingGeometry = new List<RuntimeOrientedBox>();
            var crosswalkMarkingGeometry =
                new List<RuntimeOrientedBox>();
            var sidewalkWalkableRectangles = new List<Rect>();
            var crosswalkWalkableRectangles = new List<Rect>();
            var crosswalks = new List<CityCrosswalkDescriptor>();
            var markingExclusions = new List<Rect>();
            var edgesWithSidewalks = new HashSet<RoadEdge>();

            Dictionary<Vector2Int, NodeConnections> connections =
                CreateNodeConnections(layout);
            var busIntersections = new HashSet<Vector2Int>(
                CityBusIntersectionSelector.Select(layout));
            List<RoadEdge> sortedEdges = CreateSortedEdges(layout);
            CreateBaseSurfaces(
                layout,
                sortedEdges,
                streetSurfaces,
                parkPaths,
                streetGeometry,
                parkPathGeometry);
            CreateSidewalkStrips(
                layout,
                sortedEdges,
                connections,
                busIntersections,
                sidewalks,
                sidewalkGeometry,
                sidewalkWalkableRectangles,
                edgesWithSidewalks);
            CreateIntersectionSidewalks(
                layout,
                connections,
                busIntersections,
                sidewalks,
                sidewalkGeometry,
                sidewalkWalkableRectangles,
                markingExclusions);

            IReadOnlyList<Vector2Int> selectedNodes =
                CityStreetIntersectionSelector.Select(
                    layout,
                    MaximumCrosswalkIntersections);
            CreateCrosswalks(
                layout,
                selectedNodes,
                sortedEdges,
                edgesWithSidewalks,
                carriagewayWidth,
                crosswalkMarkings,
                crosswalkMarkingGeometry,
                crosswalkWalkableRectangles,
                crosswalks,
                markingExclusions);
            CreateCenterMarkings(
                layout,
                sortedEdges,
                markingExclusions,
                centerMarkings,
                centerMarkingGeometry);

            return new CityStreetSurfacePlan(
                carriagewayWidth,
                streetSurfaces,
                parkPaths,
                sidewalks,
                centerMarkings,
                crosswalkMarkings,
                streetGeometry,
                parkPathGeometry,
                sidewalkGeometry,
                centerMarkingGeometry,
                crosswalkMarkingGeometry,
                sidewalkWalkableRectangles,
                crosswalkWalkableRectangles,
                new List<Vector2Int>(selectedNodes),
                crosswalks);
        }

        private static void CreateBaseSurfaces(
            CityLayout layout,
            IReadOnlyList<RoadEdge> sortedEdges,
            ICollection<Bounds> streetSurfaces,
            ICollection<Bounds> parkPaths,
            ICollection<RuntimeOrientedBox> streetGeometry,
            ICollection<RuntimeOrientedBox> parkPathGeometry)
        {
            var streetNodes = new HashSet<Vector2Int>();
            var parkNodes = new HashSet<Vector2Int>();
            float halfRoad = layout.RoadWidth * 0.5f;
            for (int index = 0; index < sortedEdges.Count; index++)
            {
                RoadEdge edge = sortedEdges[index];
                Vector3 start = layout.GetNodeWorldPosition(edge.A);
                Vector3 end = layout.GetNodeWorldPosition(edge.B);
                Vector3 delta = end - start;
                float planarLength = new Vector2(
                    delta.x,
                    delta.z).magnitude;
                float insetAmount = planarLength > GeometryTolerance
                    ? Mathf.Clamp01(halfRoad / planarLength)
                    : 0f;
                Vector3 segmentStart = Vector3.Lerp(
                    start,
                    end,
                    insetAmount);
                Vector3 segmentEnd = Vector3.Lerp(
                    start,
                    end,
                    1f - insetAmount);
                segmentStart.y = start.y;
                segmentEnd.y = end.y;
                bool isStreet = layout.GetPathKind(edge) ==
                                CityPathKind.Street;
                bool hasSignatureStair = isStreet &&
                    layout.ElevationPlan.TryGetSignatureStair(
                        edge,
                        out _);
                float surfaceWidth = hasSignatureStair
                    ? layout.RoadWidth - (SidewalkWidth * 2f)
                    : layout.RoadWidth;
                RuntimeOrientedBox geometry = CreateSurfaceBox(
                    segmentStart + Vector3.up * RoadTop,
                    segmentEnd + Vector3.up * RoadTop,
                    surfaceWidth,
                    RoadSurfaceHeight);
                Vector3 size = edge.IsHorizontal
                    ? new Vector3(
                        Mathf.Abs(delta.x) + layout.RoadWidth,
                        RoadSurfaceHeight,
                        surfaceWidth)
                    : new Vector3(
                        surfaceWidth,
                        RoadSurfaceHeight,
                        Mathf.Abs(delta.z) + layout.RoadWidth);
                var surface = new Bounds((start + end) * 0.5f, size);
                if (!isStreet)
                {
                    parkPaths.Add(surface);
                    parkPathGeometry.Add(geometry);
                    parkNodes.Add(edge.A);
                    parkNodes.Add(edge.B);
                }
                else
                {
                    streetSurfaces.Add(surface);
                    streetGeometry.Add(geometry);
                    streetNodes.Add(edge.A);
                    streetNodes.Add(edge.B);
                }
            }

            foreach (Vector2Int node in streetNodes)
            {
                Vector3 center = layout.GetNodeWorldPosition(node);
                streetGeometry.Add(new RuntimeOrientedBox(
                    center,
                    Quaternion.identity,
                    new Vector3(
                        layout.RoadWidth,
                        RoadSurfaceHeight,
                        layout.RoadWidth)));
            }

            foreach (Vector2Int node in parkNodes)
            {
                if (streetNodes.Contains(node))
                {
                    continue;
                }

                Vector3 center = layout.GetNodeWorldPosition(node);
                parkPathGeometry.Add(new RuntimeOrientedBox(
                    center,
                    Quaternion.identity,
                    new Vector3(
                        layout.RoadWidth,
                        RoadSurfaceHeight,
                        layout.RoadWidth)));
            }
        }

        private static void CreateSidewalkStrips(
            CityLayout layout,
            IReadOnlyList<RoadEdge> sortedEdges,
            IReadOnlyDictionary<Vector2Int, NodeConnections> connections,
            ISet<Vector2Int> busIntersections,
            ICollection<Bounds> sidewalks,
            ICollection<RuntimeOrientedBox> sidewalkGeometry,
            ICollection<Rect> walkableRectangles,
            ISet<RoadEdge> edgesWithSidewalks)
        {
            float halfRoad = layout.RoadWidth * 0.5f;
            float sideOffset = halfRoad - (SidewalkWidth * 0.5f);
            for (int index = 0; index < sortedEdges.Count; index++)
            {
                RoadEdge edge = sortedEdges[index];
                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                Vector3 roadStart = layout.GetNodeWorldPosition(edge.A);
                Vector3 roadEnd = layout.GetNodeWorldPosition(edge.B);
                Vector3 delta = roadEnd - roadStart;
                float planarLength = new Vector2(
                    delta.x,
                    delta.z).magnitude;
                Vector3 tangent = new Vector3(
                    delta.x,
                    0f,
                    delta.z).normalized;
                float startInset = ResolveEndpointInset(
                    connections[edge.A],
                    halfRoad) +
                    (busIntersections.Contains(edge.A)
                        ? BusApproachApronLength
                        : 0f);
                float endInset = ResolveEndpointInset(
                    connections[edge.B],
                    halfRoad) +
                    (busIntersections.Contains(edge.B)
                        ? BusApproachApronLength
                        : 0f);
                Vector3 start = Vector3.Lerp(
                    roadStart,
                    roadEnd,
                    Mathf.Clamp01(startInset / planarLength));
                Vector3 end = Vector3.Lerp(
                    roadStart,
                    roadEnd,
                    1f - Mathf.Clamp01(endInset / planarLength));
                start.y = layout.ElevationPlan.SampleRoadDatum(
                    edge,
                    Mathf.Clamp01(startInset / planarLength));
                end.y = layout.ElevationPlan.SampleRoadDatum(
                    edge,
                    1f - Mathf.Clamp01(endInset / planarLength));
                float length = Vector3.Distance(start, end);
                if (length <= GeometryTolerance)
                {
                    throw new InvalidOperationException(
                        $"Street edge {edge} is too short for sidewalks.");
                }

                Vector3 left = new Vector3(
                    -tangent.z,
                    0f,
                    tangent.x);
                bool hasStair = layout.ElevationPlan.TryGetSignatureStair(
                    edge,
                    out CityElevationStairDescriptor stair);
                bool stairOnLeft = false;
                CityElevationStairPlacement stairPlacement = null;
                if (hasStair)
                {
                    stairPlacement =
                        CityElevationStairPlacementPlanner.Create(
                            layout,
                            stair);
                    stairOnLeft = Vector3.Dot(
                        stairPlacement.SideDirection,
                        left) > 0f;
                }

                if (!hasStair || !stairOnLeft)
                {
                    AddSidewalk(
                        CreateSurfaceBox(
                            start + left * sideOffset +
                            Vector3.up * SidewalkTop,
                            end + left * sideOffset +
                            Vector3.up * SidewalkTop,
                            SidewalkWidth,
                            SidewalkHeight),
                        sidewalks,
                        sidewalkGeometry,
                        walkableRectangles);
                }

                if (!hasStair || stairOnLeft)
                {
                    AddSidewalk(
                        CreateSurfaceBox(
                            start - left * sideOffset +
                            Vector3.up * SidewalkTop,
                            end - left * sideOffset +
                            Vector3.up * SidewalkTop,
                            SidewalkWidth,
                            SidewalkHeight),
                        sidewalks,
                        sidewalkGeometry,
                        walkableRectangles);
                }

                if (hasStair)
                {
                    AddSignatureStairApproaches(
                        stair,
                        stairPlacement,
                        sidewalks,
                        sidewalkGeometry,
                        walkableRectangles);
                }
                edgesWithSidewalks.Add(edge);
            }
        }

        private static void AddSignatureStairApproaches(
            CityElevationStairDescriptor stair,
            CityElevationStairPlacement placement,
            ICollection<Bounds> sidewalks,
            ICollection<RuntimeOrientedBox> sidewalkGeometry,
            ICollection<Rect> walkableRectangles)
        {
            AddApproachIfLongEnough(
                placement.LowerApproachStart,
                placement.LowerApproachEnd,
                stair.Width,
                sidewalks,
                sidewalkGeometry,
                walkableRectangles);
            AddApproachIfLongEnough(
                placement.UpperApproachStart,
                placement.UpperApproachEnd,
                stair.Width,
                sidewalks,
                sidewalkGeometry,
                walkableRectangles);
            walkableRectangles.Add(placement.Footprint);
        }

        private static void AddApproachIfLongEnough(
            Vector3 first,
            Vector3 second,
            float width,
            ICollection<Bounds> sidewalks,
            ICollection<RuntimeOrientedBox> sidewalkGeometry,
            ICollection<Rect> walkableRectangles)
        {
            if (Vector3.Distance(first, second) <= GeometryTolerance)
            {
                return;
            }

            AddSidewalk(
                CreateSurfaceBox(
                    first,
                    second,
                    width,
                    SidewalkHeight),
                sidewalks,
                sidewalkGeometry,
                walkableRectangles);
        }

        private static void CreateIntersectionSidewalks(
            CityLayout layout,
            IReadOnlyDictionary<Vector2Int, NodeConnections> connections,
            ISet<Vector2Int> busIntersections,
            ICollection<Bounds> sidewalks,
            ICollection<RuntimeOrientedBox> sidewalkGeometry,
            ICollection<Rect> walkableRectangles,
            ICollection<Rect> markingExclusions)
        {
            float halfRoad = layout.RoadWidth * 0.5f;
            float carriagewayWidth =
                layout.RoadWidth - (SidewalkWidth * 2f);
            float halfCarriageway = carriagewayWidth * 0.5f;
            float sideOffset =
                halfCarriageway + (SidewalkWidth * 0.5f);
            List<Vector2Int> nodes = CreateSortedNodes(layout);
            for (int index = 0; index < nodes.Count; index++)
            {
                Vector2Int node = nodes[index];
                NodeConnections nodeConnections = connections[node];
                if (!nodeConnections.IsIntersectionCore)
                {
                    continue;
                }

                Vector3 nodePosition = layout.GetNodeWorldPosition(node);
                markingExclusions.Add(Rect.MinMaxRect(
                    nodePosition.x - halfRoad,
                    nodePosition.z - halfRoad,
                    nodePosition.x + halfRoad,
                    nodePosition.z + halfRoad));
                if (nodeConnections.StreetCount == 0)
                {
                    continue;
                }

                float cornerOffset = busIntersections.Contains(node)
                    ? CityBusIntersectionSelector
                        .GetCornerCenterOffset(layout)
                    : sideOffset;

                for (int xSign = -1; xSign <= 1; xSign += 2)
                {
                    for (int zSign = -1; zSign <= 1; zSign += 2)
                    {
                        Vector3 center = nodePosition + new Vector3(
                            xSign * cornerOffset,
                            (RoadTop + SidewalkTop) * 0.5f,
                            zSign * cornerOffset);
                        AddSidewalk(
                            new Bounds(
                                center,
                                new Vector3(
                                    SidewalkWidth,
                                    SidewalkHeight,
                                    SidewalkWidth)),
                            sidewalks,
                            sidewalkGeometry,
                            walkableRectangles);
                    }
                }

                AddClosedIntersectionMouths(
                    nodePosition,
                    nodeConnections,
                    cornerOffset,
                    sidewalks,
                    sidewalkGeometry,
                    walkableRectangles);
            }
        }

        private static void AddClosedIntersectionMouths(
            Vector3 nodePosition,
            NodeConnections connections,
            float cornerOffset,
            ICollection<Bounds> sidewalks,
            ICollection<RuntimeOrientedBox> sidewalkGeometry,
            ICollection<Rect> walkableRectangles)
        {
            float mouthSpan = (cornerOffset * 2f) - SidewalkWidth;
            Vector2Int[] directions =
            {
                Vector2Int.left,
                Vector2Int.right,
                Vector2Int.down,
                Vector2Int.up
            };
            for (int index = 0; index < directions.Length; index++)
            {
                Vector2Int direction = directions[index];
                if (connections.Contains(direction))
                {
                    continue;
                }

                Vector3 center = nodePosition + new Vector3(
                    direction.x * cornerOffset,
                    (RoadTop + SidewalkTop) * 0.5f,
                    direction.y * cornerOffset);
                Vector3 size = direction.x != 0
                    ? new Vector3(
                        SidewalkWidth,
                        SidewalkHeight,
                        mouthSpan)
                    : new Vector3(
                        mouthSpan,
                        SidewalkHeight,
                        SidewalkWidth);
                AddSidewalk(
                    new Bounds(center, size),
                    sidewalks,
                    sidewalkGeometry,
                    walkableRectangles);
            }
        }

        private static void CreateCrosswalks(
            CityLayout layout,
            IReadOnlyList<Vector2Int> selectedNodes,
            IReadOnlyList<RoadEdge> sortedEdges,
            ISet<RoadEdge> edgesWithSidewalks,
            float carriagewayWidth,
            ICollection<Bounds> crosswalkMarkings,
            ICollection<RuntimeOrientedBox> crosswalkMarkingGeometry,
            ICollection<Rect> walkableRectangles,
            ICollection<CityCrosswalkDescriptor> crosswalks,
            ICollection<Rect> markingExclusions)
        {
            float halfRoad = layout.RoadWidth * 0.5f;
            for (int nodeIndex = 0;
                 nodeIndex < selectedNodes.Count;
                 nodeIndex++)
            {
                Vector2Int node = selectedNodes[nodeIndex];
                Vector3 nodePosition = layout.GetNodeWorldPosition(node);
                for (int edgeIndex = 0;
                     edgeIndex < sortedEdges.Count;
                     edgeIndex++)
                {
                    RoadEdge edge = sortedEdges[edgeIndex];
                    if (!edge.Contains(node) ||
                        layout.GetPathKind(edge) != CityPathKind.Street ||
                        !edgesWithSidewalks.Contains(edge))
                    {
                        continue;
                    }

                    Vector2Int other = edge.Other(node);
                    Vector3 otherPosition =
                        layout.GetNodeWorldPosition(other);
                    Vector3 outward = otherPosition - nodePosition;
                    outward.y = 0f;
                    outward.Normalize();
                    Vector3 crosswalkCenter = nodePosition +
                        (outward *
                         (halfRoad + (CrosswalkDepth * 0.5f)));
                    TrySetRoadDatum(layout, ref crosswalkCenter);
                    Rect walkable = CreateOrientedRect(
                        crosswalkCenter,
                        outward,
                        CrosswalkDepth,
                        carriagewayWidth);
                    walkableRectangles.Add(walkable);
                    crosswalks.Add(new CityCrosswalkDescriptor(
                        node,
                        edge,
                        walkable,
                        crosswalkCenter,
                        outward,
                        new Vector3(-outward.z, 0f, outward.x)));
                    markingExclusions.Add(walkable);

                    for (int stripeIndex = 0;
                         stripeIndex < CrosswalkStripeCount;
                         stripeIndex++)
                    {
                        float distance = halfRoad +
                            (((stripeIndex + 0.5f) /
                              CrosswalkStripeCount) * CrosswalkDepth);
                        Vector3 center = nodePosition +
                            (outward * distance);
                        TrySetRoadDatum(layout, ref center);
                        center.y += MarkingCenterAboveRoadBase;
                        Vector3 size = Mathf.Abs(outward.x) > 0.5f
                            ? new Vector3(
                                CrosswalkStripeDepth,
                                MarkingHeight,
                                carriagewayWidth)
                            : new Vector3(
                                carriagewayWidth,
                                MarkingHeight,
                                CrosswalkStripeDepth);
                        var marking = new Bounds(center, size);
                        crosswalkMarkings.Add(marking);
                        crosswalkMarkingGeometry.Add(
                            new RuntimeOrientedBox(
                                marking.center,
                                Quaternion.identity,
                                marking.size));
                    }
                }
            }
        }

        private static void CreateCenterMarkings(
            CityLayout layout,
            IReadOnlyList<RoadEdge> sortedEdges,
            IReadOnlyList<Rect> markingExclusions,
            ICollection<Bounds> centerMarkings,
            ICollection<RuntimeOrientedBox> centerMarkingGeometry)
        {
            for (int edgeIndex = 0;
                 edgeIndex < sortedEdges.Count;
                 edgeIndex++)
            {
                RoadEdge edge = sortedEdges[edgeIndex];
                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                Vector3 start = layout.GetNodeWorldPosition(edge.A);
                Vector3 end = layout.GetNodeWorldPosition(edge.B);
                float planarLength = new Vector2(
                    end.x - start.x,
                    end.z - start.z).magnitude;
                float length = Vector3.Distance(start, end);
                int dashCount = Mathf.Max(
                    2,
                    Mathf.FloorToInt(length / 5f));
                for (int dashIndex = 0;
                     dashIndex < dashCount;
                     dashIndex++)
                {
                    float t = (dashIndex + 0.5f) / dashCount;
                    float dashLength = Mathf.Min(
                        MaximumCenterDashLength,
                        (length / dashCount) * 0.48f);
                    float halfAmount = planarLength > GeometryTolerance
                        ? dashLength * 0.5f / planarLength
                        : 0f;
                    Vector3 dashStart = Vector3.Lerp(
                        start,
                        end,
                        Mathf.Clamp01(t - halfAmount));
                    Vector3 dashEnd = Vector3.Lerp(
                        start,
                        end,
                        Mathf.Clamp01(t + halfAmount));
                    dashStart.y = layout.ElevationPlan.SampleRoadDatum(
                        edge,
                        Mathf.Clamp01(t - halfAmount));
                    dashEnd.y = layout.ElevationPlan.SampleRoadDatum(
                        edge,
                        Mathf.Clamp01(t + halfAmount));
                    float markingTop =
                        MarkingCenterAboveRoadBase +
                        MarkingHeight * 0.5f;
                    RuntimeOrientedBox geometry = CreateSurfaceBox(
                        dashStart + Vector3.up * markingTop,
                        dashEnd + Vector3.up * markingTop,
                        CenterDashWidth,
                        MarkingHeight);
                    Bounds dash = CalculateBounds(geometry);
                    if (!OverlapsAny(
                            CreateRect(dash),
                            markingExclusions))
                    {
                        centerMarkings.Add(dash);
                        centerMarkingGeometry.Add(geometry);
                    }
                }
            }
        }

        private static void AddSidewalk(
            Bounds sidewalk,
            ICollection<Bounds> sidewalks,
            ICollection<RuntimeOrientedBox> sidewalkGeometry,
            ICollection<Rect> walkableRectangles)
        {
            sidewalks.Add(sidewalk);
            sidewalkGeometry.Add(new RuntimeOrientedBox(
                sidewalk.center,
                Quaternion.identity,
                sidewalk.size));
            walkableRectangles.Add(CreateRect(sidewalk));
        }

        private static void AddSidewalk(
            RuntimeOrientedBox sidewalk,
            ICollection<Bounds> sidewalks,
            ICollection<RuntimeOrientedBox> sidewalkGeometry,
            ICollection<Rect> walkableRectangles)
        {
            Bounds bounds = CalculateBounds(sidewalk);
            sidewalks.Add(bounds);
            sidewalkGeometry.Add(sidewalk);
            walkableRectangles.Add(CreateRect(bounds));
        }

        private static RuntimeOrientedBox CreateSurfaceBox(
            Vector3 topStart,
            Vector3 topEnd,
            float width,
            float thickness)
        {
            Vector3 slope = topEnd - topStart;
            if (slope.sqrMagnitude <= GeometryTolerance)
            {
                throw new InvalidOperationException(
                    "A graded surface requires a positive run length.");
            }

            Quaternion rotation = Quaternion.LookRotation(
                slope.normalized,
                Vector3.up);
            Vector3 normal = rotation * Vector3.up;
            return new RuntimeOrientedBox(
                (topStart + topEnd) * 0.5f -
                normal * (thickness * 0.5f),
                rotation,
                new Vector3(width, thickness, slope.magnitude));
        }

        private static void TrySetRoadDatum(
            CityLayout layout,
            ref Vector3 position)
        {
            if (layout.ElevationPlan.TrySampleSurface(
                    new Vector2(position.x, position.z),
                    CitySurfaceRole.RoadDatum,
                    out float height,
                    out _))
            {
                position.y = height;
            }
        }

        private static Bounds CalculateBounds(RuntimeOrientedBox box)
        {
            Vector3 half = box.Size * 0.5f;
            Vector3 right = box.Rotation * Vector3.right;
            Vector3 up = box.Rotation * Vector3.up;
            Vector3 forward = box.Rotation * Vector3.forward;
            Vector3 extents = Abs(right) * half.x +
                              Abs(up) * half.y +
                              Abs(forward) * half.z;
            return new Bounds(box.Center, extents * 2f);
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
        }

        private static Rect CreateOrientedRect(
            Vector3 center,
            Vector3 direction,
            float alongLength,
            float acrossLength)
        {
            return Mathf.Abs(direction.x) > 0.5f
                ? new Rect(
                    center.x - (alongLength * 0.5f),
                    center.z - (acrossLength * 0.5f),
                    alongLength,
                    acrossLength)
                : new Rect(
                    center.x - (acrossLength * 0.5f),
                    center.z - (alongLength * 0.5f),
                    acrossLength,
                    alongLength);
        }

        private static Rect CreateRect(Bounds bounds)
        {
            return Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z);
        }

        private static float ResolveEndpointInset(
            NodeConnections connections,
            float halfRoad)
        {
            if (connections.IsIntersectionCore)
            {
                return halfRoad;
            }

            return connections.Count == 1 ? -halfRoad : 0f;
        }

        private static bool OverlapsAny(
            Rect rectangle,
            IReadOnlyList<Rect> others)
        {
            for (int index = 0; index < others.Count; index++)
            {
                Rect other = others[index];
                if (Mathf.Min(rectangle.xMax, other.xMax) -
                        Mathf.Max(rectangle.xMin, other.xMin) >
                    GeometryTolerance &&
                    Mathf.Min(rectangle.yMax, other.yMax) -
                        Mathf.Max(rectangle.yMin, other.yMin) >
                    GeometryTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<Vector2Int, NodeConnections>
            CreateNodeConnections(CityLayout layout)
        {
            var connections =
                new Dictionary<Vector2Int, NodeConnections>(
                    layout.Nodes.Count);
            for (int index = 0; index < layout.Nodes.Count; index++)
            {
                connections.Add(
                    layout.Nodes[index],
                    new NodeConnections());
            }

            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                bool isStreet =
                    layout.GetPathKind(edge) == CityPathKind.Street;
                connections[edge.A].Add(edge.B - edge.A, isStreet);
                connections[edge.B].Add(edge.A - edge.B, isStreet);
            }

            return connections;
        }

        private static List<RoadEdge> CreateSortedEdges(CityLayout layout)
        {
            var edges = new List<RoadEdge>(layout.RoadEdges);
            edges.Sort(RoadEdge.Compare);
            return edges;
        }

        private static List<Vector2Int> CreateSortedNodes(CityLayout layout)
        {
            var nodes = new List<Vector2Int>(layout.Nodes);
            nodes.Sort(CompareNodes);
            return nodes;
        }

        private static int CompareNodes(Vector2Int left, Vector2Int right)
        {
            int xComparison = left.x.CompareTo(right.x);
            return xComparison != 0
                ? xComparison
                : left.y.CompareTo(right.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private sealed class NodeConnections
        {
            private readonly List<Vector2Int> directions =
                new List<Vector2Int>(4);

            public int Count => directions.Count;
            public int StreetCount { get; private set; }

            public bool IsIntersectionCore
            {
                get
                {
                    if (directions.Count >= 3)
                    {
                        return true;
                    }

                    return directions.Count == 2 &&
                           directions[0] + directions[1] !=
                           Vector2Int.zero;
                }
            }

            public void Add(Vector2Int direction, bool isStreet)
            {
                directions.Add(direction);
                if (isStreet)
                {
                    StreetCount++;
                }
            }

            public bool Contains(Vector2Int direction)
            {
                return directions.Contains(direction);
            }
        }
    }
}
