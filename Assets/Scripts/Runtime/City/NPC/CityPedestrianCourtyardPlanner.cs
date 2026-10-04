using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static partial class CityPedestrianPlanner
    {
        private const float CourtyardKnotTolerance = 0.001f;

        private static void BuildCourtyardPaths(CityLayout layout,
            CityStreetSurfacePlan surfaces, GraphBuilder graph)
        {
            var courts = new List<CityCourtyardBlock>(layout.CourtyardBlocks);
            courts.Sort((a, b) => a.Cell.x != b.Cell.x
                ? a.Cell.x.CompareTo(b.Cell.x) : a.Cell.y.CompareTo(b.Cell.y));
            var courtNodes = new Dictionary<Vector2Int, CourtyardNodes>();
            foreach (CityCourtyardBlock block in courts)
            {
                if (!layout.TryGetFrontageEdge(block.Primary, out RoadEdge frontage))
                    throw new InvalidOperationException("A pedestrian courtyard requires a real street frontage.");
                string prefix = $"courtyard:{block.Cell.x}:{block.Cell.y}";
                Func<Vector2, float> sampleHeight = point =>
                    CourtyardHeight(layout, surfaces, point, frontage);
                int gate = graph.SplitSidewalkAt(prefix + ":gate", frontage,
                    block.Route.Vertices[0], sampleHeight);
                var nodes = new CourtyardNodes(graph, prefix, sampleHeight);
                nodes.Register(XZ(graph.Nodes[gate].Position), gate);
                int court = graph.AddNode(prefix + ":court", block.CourtCenter, false);
                nodes.Register(XZ(block.CourtCenter), court);
                courtNodes.Add(block.Cell, nodes);
                graph.AddNavigationPolygons(block.GroundPolygons);

                int previous = gate;
                for (int i = 0; i < block.Route.Vertices.Count; i++)
                {
                    int next = nodes.GetOrAdd(block.Route.Vertices[i], $"knot:{i}");
                    AddCourtyardLink(graph, $"{prefix}:walk:{i}", previous, next, sampleHeight);
                    previous = next;
                }
                AddCourtyardLink(graph, prefix + ":walk:close", previous, gate, sampleHeight);
            }

            foreach (CityCourtyardConnection connection in layout.CourtyardConnections)
            {
                string prefix = $"courtyard-connection:{connection.FirstCell.x}:{connection.FirstCell.y}:" +
                    $"{connection.SecondCell.x}:{connection.SecondCell.y}";
                Func<Vector2, float> sampleHeight = point => CourtyardHeight(layout, surfaces, point, null);
                IReadOnlyList<Vector2> points = connection.Path.Vertices;
                int previous = courtNodes[connection.FirstCell].GetOrAdd(points[0], "court");
                for (int i = 1; i < points.Count; i++)
                {
                    int next = i == points.Count - 1
                        ? courtNodes[connection.SecondCell].GetOrAdd(points[i], "court")
                        : graph.AddNode($"{prefix}:knot:{i}", GroundedPoint(points[i], sampleHeight), false);
                    AddCourtyardLink(graph, $"{prefix}:walk:{i - 1}", previous, next, sampleHeight);
                    previous = next;
                }
            }
        }

        private static void AddCourtyardLink(GraphBuilder graph, string id,
            int first, int second, Func<Vector2, float> sampleHeight)
        {
            if (first == second) return;
            graph.AddLink(id, first, second, CityPedestrianLinkKind.Courtyard, false,
                new CityRoadPath(new[] { XZ(graph.Nodes[first].Position), XZ(graph.Nodes[second].Position) }),
                sampleHeight);
        }

        private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);

        private static Vector3 GroundedPoint(Vector2 point, Func<Vector2, float> sampleHeight) =>
            new Vector3(point.x, sampleHeight(point), point.y);

        private static float CourtyardHeight(CityLayout layout, CityStreetSurfacePlan surfaces,
            Vector2 point, RoadEdge? accessEdge)
        {
            var position = new Vector3(point.x, 0f, point.y);
            foreach (RuntimeOrientedBox sidewalk in surfaces.SidewalkGeometry)
                if (sidewalk.TrySampleTop(position, out float top)) return top;
            CityRoadJunction junction = layout.RoadGeometry.ObliqueJunction;
            if (junction != null)
                foreach (Vector2[] pavement in junction.SidewalkPolygons)
                    if (CityRoadPolygon.Contains(pavement, point))
                        return layout.GetNodeWorldPosition(junction.Node).y + CityStreetSurfacePlanner.SidewalkTop;
            foreach (Vector2[] pavement in surfaces.CurvedSidewalkPolygons)
                if (CityRoadPolygon.Contains(pavement, point))
                {
                    RoadEdge closest = default;
                    float distanceSquared = float.PositiveInfinity;
                    foreach (RoadEdge edge in layout.RoadGeometry.CurvedEdges)
                    {
                        float candidate = layout.RoadGeometry.Get(edge).Project(point).DistanceSquared;
                        if (candidate < distanceSquared) { distanceSquared = candidate; closest = edge; }
                    }
                    if (!float.IsPositiveInfinity(distanceSquared))
                        return CurvedSidewalkHeight(layout, surfaces, closest, point);
                }
            if (CityTerrainSurfacePlan.TrySampleGroundTop(layout, point, out float ground, out _))
                return ground;
            if (accessEdge.HasValue)
                return CurvedSidewalkHeight(layout, surfaces, accessEdge.Value, point);
            throw new InvalidOperationException($"A courtyard shortcut has no physical ground at {point}.");
        }

        private sealed class CourtyardNodes
        {
            private readonly GraphBuilder graph;
            private readonly string prefix;
            private readonly Func<Vector2, float> sampleHeight;
            private readonly List<Vector2> positions = new List<Vector2>();
            private readonly List<int> indices = new List<int>();

            public CourtyardNodes(GraphBuilder graph, string prefix, Func<Vector2, float> sampleHeight)
            { this.graph = graph; this.prefix = prefix; this.sampleHeight = sampleHeight; }

            public void Register(Vector2 point, int index)
            { positions.Add(point); indices.Add(index); }

            public int GetOrAdd(Vector2 point, string suffix)
            {
                for (int i = 0; i < positions.Count; i++)
                    if ((point - positions[i]).sqrMagnitude <= CourtyardKnotTolerance * CourtyardKnotTolerance)
                        return indices[i];
                int index = graph.AddNode(prefix + ":" + suffix, GroundedPoint(point, sampleHeight), false);
                Register(point, index);
                return index;
            }
        }

        private sealed partial class GraphBuilder
        {
            // Splitting happens after normal street construction. Existing spawn
            // identities and positions remain; each anchor points at its real slice.
            public int SplitSidewalkAt(string id, RoadEdge edge, Vector2 arrival,
                Func<Vector2, float> sampleHeight)
            {
                string prefix = $"sidewalk:{EdgeId(edge)}:";
                int selected = -1;
                CityRoadPath selectedPath = null;
                CityRoadProjection projection = default;
                float best = float.PositiveInfinity;
                for (int i = 0; i < links.Count; i++)
                {
                    CityPedestrianLink candidate = links[i];
                    if (candidate.Kind != CityPedestrianLinkKind.Sidewalk ||
                        !candidate.Id.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    CityRoadPath path = candidate.Path ?? new CityRoadPath(new[] {
                        XZ(nodes[candidate.FirstNodeIndex].Position), XZ(nodes[candidate.SecondNodeIndex].Position) });
                    CityRoadProjection projected = path.Project(arrival);
                    if (projected.DistanceSquared >= best) continue;
                    selected = i; selectedPath = path; projection = projected; best = projected.DistanceSquared;
                }
                if (selected < 0 || best > CityStreetSurfacePlanner.SidewalkWidth *
                    CityStreetSurfacePlanner.SidewalkWidth * .25f)
                    throw new InvalidOperationException($"Courtyard gate {id} has no adjacent physical sidewalk lane.");
                CityPedestrianLink original = links[selected];
                float along = projection.DistanceAlong;
                float station = 0f;
                for (int i = 0; i < selectedPath.Vertices.Count; i++)
                {
                    if (i > 0) station += Vector2.Distance(selectedPath.Vertices[i - 1], selectedPath.Vertices[i]);
                    if (Mathf.Abs(station - along) <= CourtyardKnotTolerance) { along = station; break; }
                }
                if (along <= CourtyardKnotTolerance) return original.FirstNodeIndex;
                if (along >= selectedPath.Length - CourtyardKnotTolerance) return original.SecondNodeIndex;
                Vector2 gatePosition = selectedPath.SampleDistance(along).Position;
                // A straight lane's endpoint interpolation can miss the raised
                // pavement at an interior gate. Use the actual surface here,
                // just as the adjoining courtyard links do.
                float height = sampleHeight(gatePosition);
                int gate = AddNode(id, new Vector3(gatePosition.x, height, gatePosition.y), false);
                IReadOnlyList<Vector2[]> polygonOverride = navigationPolygonOverrides[selected];
                links.RemoveAt(selected);
                navigationRectangles.RemoveAt(selected);
                navigationPolygonOverrides.RemoveAt(selected);
                linkKeys.Remove(new LinkKey(original.FirstNodeIndex, original.SecondNodeIndex, original.Kind));
                AddLink(original.Id + ":court:a", original.FirstNodeIndex, gate, original.Kind, false,
                    original.Path == null ? null : SlicePath(selectedPath, 0f, along),
                    original.PathHeightSampler, polygonOverride);
                AddLink(original.Id + ":court:b", gate, original.SecondNodeIndex, original.Kind, false,
                    original.Path == null ? null : SlicePath(selectedPath, along, selectedPath.Length),
                    original.PathHeightSampler, polygonOverride);
                for (int i = 0; i < spawnAnchors.Count; i++)
                {
                    CityPedestrianSpawnAnchor anchor = spawnAnchors[i];
                    if (anchor.FirstNodeIndex != original.FirstNodeIndex ||
                        anchor.SecondNodeIndex != original.SecondNodeIndex) continue;
                    bool firstSlice = selectedPath.Project(XZ(anchor.Position)).DistanceAlong <= along;
                    spawnAnchors[i] = new CityPedestrianSpawnAnchor(anchor.Id, anchor.Position,
                        firstSlice ? original.FirstNodeIndex : gate,
                        firstSlice ? gate : original.SecondNodeIndex);
                }
                return gate;
            }
        }
    }
}
