using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityRoadSample
    {
        internal CityRoadSample(Vector2 position, Vector2 tangent, float distance)
        { Position = position; Tangent = tangent; Distance = distance; }
        public Vector2 Position { get; }
        public Vector2 Tangent { get; }
        public Vector2 Right => new Vector2(Tangent.y, -Tangent.x);
        public float Distance { get; }
    }

    public readonly struct CityRoadProjection
    {
        internal CityRoadProjection(Vector2 position, float along, float lateral, float squared)
        { Position = position; DistanceAlong = along; SignedLateral = lateral; DistanceSquared = squared; }
        public Vector2 Position { get; }
        public float DistanceAlong { get; }
        public float SignedLateral { get; }
        public float DistanceSquared { get; }
    }

    /// <summary>Immutable world-XZ path. Every offset lane owns its own metre distances.</summary>
    public sealed class CityRoadPath
    {
        private readonly float[] distances;
        public CityRoadPath(IList<Vector2> vertices)
        {
            if (vertices == null || vertices.Count < 2)
                throw new ArgumentException("A road path needs two or more vertices.", nameof(vertices));
            var copy = new List<Vector2>(vertices);
            distances = new float[copy.Count];
            for (int i = 1; i < copy.Count; i++)
            {
                float span = Vector2.Distance(copy[i - 1], copy[i]);
                if (span < .001f) throw new ArgumentException("A road path has a zero length span.", nameof(vertices));
                distances[i] = distances[i - 1] + span;
            }
            Vertices = new ReadOnlyCollection<Vector2>(copy);
            Length = distances[distances.Length - 1];
            Bounds = CityRoadPolygon.Bounds(copy);
        }
        public IReadOnlyList<Vector2> Vertices { get; }
        public float Length { get; }
        public Rect Bounds { get; }
        public bool IsStraight => Vertices.Count == 2;
        public CityRoadSample SampleDistance(float distance)
        {
            distance = Mathf.Clamp(distance, 0, Length);
            int segment = 1;
            while (segment < distances.Length - 1 && distances[segment] < distance) segment++;
            Vector2 delta = Vertices[segment] - Vertices[segment - 1];
            float amount = (distance - distances[segment - 1]) / (distances[segment] - distances[segment - 1]);
            return new CityRoadSample(Vertices[segment - 1] + delta * amount, delta.normalized, distance);
        }
        public CityRoadProjection Project(Vector2 point)
        {
            float best = float.PositiveInfinity;
            CityRoadProjection result = default;
            for (int i = 1; i < Vertices.Count; i++)
            {
                Vector2 delta = Vertices[i] - Vertices[i - 1];
                float amount = Mathf.Clamp01(Vector2.Dot(point - Vertices[i - 1], delta) / delta.sqrMagnitude);
                Vector2 position = Vertices[i - 1] + delta * amount;
                float squared = (point - position).sqrMagnitude;
                if (squared >= best) continue;
                Vector2 right = new Vector2(delta.y, -delta.x).normalized;
                result = new CityRoadProjection(position, distances[i - 1] + delta.magnitude * amount,
                    Vector2.Dot(point - position, right), squared);
                best = squared;
            }
            return result;
        }
        public CityRoadPath Reversed()
        {
            var vertices = new List<Vector2>(Vertices); vertices.Reverse();
            return new CityRoadPath(vertices);
        }
        public CityRoadPath Offset(float right)
        {
            var vertices = new List<Vector2>();
            for (int i = 0; i < Vertices.Count; i++) vertices.Add(Vertices[i] + Miter(i) * right);
            return new CityRoadPath(vertices);
        }
        private Vector2 Miter(int index)
        {
            Vector2 before = (Vertices[Mathf.Max(1, index)] - Vertices[Mathf.Max(0, index - 1)]).normalized;
            Vector2 after = (Vertices[Mathf.Min(Vertices.Count - 1, index + 1)] - Vertices[Mathf.Min(Vertices.Count - 2, index)]).normalized;
            Vector2 n0 = new Vector2(before.y, -before.x);
            Vector2 n1 = new Vector2(after.y, -after.x);
            Vector2 bisector = (n0 + n1).normalized;
            return bisector / Mathf.Max(.5f, Vector2.Dot(bisector, n1));
        }
        public IReadOnlyList<Vector2[]> Ribbon(float width, float rightOffset = 0, float endInset = 0)
        {
            CityRoadPath path = this;
            if (endInset > .001f)
            {
                float inset = Mathf.Min(endInset, Length * .45f);
                var trimmed = new List<Vector2> { SampleDistance(inset).Position };
                for (int i = 1; i < Vertices.Count - 1; i++)
                    if (distances[i] > inset + .001f && distances[i] < Length - inset - .001f) trimmed.Add(Vertices[i]);
                trimmed.Add(SampleDistance(Length - inset).Position);
                path = new CityRoadPath(trimmed);
            }
            CityRoadPath left = path.Offset(rightOffset - width * .5f);
            CityRoadPath right = path.Offset(rightOffset + width * .5f);
            var polygons = new List<Vector2[]>();
            for (int i = 1; i < path.Vertices.Count; i++)
                polygons.Add(CityRoadPolygon.CounterClockwise(new[] {
                    left.Vertices[i - 1], right.Vertices[i - 1], right.Vertices[i], left.Vertices[i] }));
            return new ReadOnlyCollection<Vector2[]>(polygons);
        }
    }

    /// <summary>Shared convex polygon operations for street ribbons and their ground complement.</summary>
    public static class CityRoadPolygon
    {
        public static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        public static float Area(IReadOnlyList<Vector2> vertices)
        {
            if (vertices.Count < 3) return 0;
            float area = 0;
            // Subtraction creates thin pieces. An origin-relative fan avoids
            // cancellation between large absolute world-coordinate products.
            for (int i = 1; i < vertices.Count - 1; i++)
                area += Cross(vertices[i] - vertices[0], vertices[i + 1] - vertices[0]);
            return area * .5f;
        }
        public static Vector2[] CounterClockwise(Vector2[] vertices)
        { if (Area(vertices) < 0) Array.Reverse(vertices); return vertices; }
        public static Vector2[] Rectangle(Rect rect) => new[] {
            new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin),
            new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax) };
        public static Rect Bounds(IReadOnlyList<Vector2> vertices)
        {
            Vector2 min = vertices[0], max = min;
            for (int i = 1; i < vertices.Count; i++) { min = Vector2.Min(min, vertices[i]); max = Vector2.Max(max, vertices[i]); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        public static bool Contains(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            if (polygon.Count < 3 || Area(polygon) <= .00001f) return false;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 edge = polygon[(i + 1) % polygon.Count] - polygon[i];
                if (Cross(edge, point - polygon[i]) < -.001f * edge.magnitude) return false;
            }
            return true;
        }
        public static List<Vector2> Clip(IReadOnlyList<Vector2> source, Vector2 a, Vector2 b, bool inside = true)
        {
            var result = new List<Vector2>();
            if (source.Count == 0) return result;
            Vector2 previous = source[source.Count - 1];
            float previousDistance = Cross(b - a, previous - a) * (inside ? 1 : -1);
            for (int i = 0; i < source.Count; i++)
            {
                Vector2 current = source[i];
                float distance = Cross(b - a, current - a) * (inside ? 1 : -1);
                if ((distance >= 0) != (previousDistance >= 0))
                    result.Add(Vector2.Lerp(previous, current, previousDistance / (previousDistance - distance)));
                if (distance >= 0) result.Add(current);
                previous = current; previousDistance = distance;
            }
            return result;
        }
        public static IReadOnlyList<Vector2[]> Subtract(IReadOnlyList<Vector2> source, IReadOnlyList<Vector2> cut)
        {
            var pieces = new List<Vector2[]>();
            IReadOnlyList<Vector2> remaining = source;
            for (int i = 0; i < cut.Count && remaining.Count >= 3; i++)
            {
                Vector2 a = cut[i], b = cut[(i + 1) % cut.Count];
                List<Vector2> outside = Clip(remaining, a, b, false);
                if (outside.Count >= 3 && Area(outside) > .00001f) pieces.Add(outside.ToArray());
                remaining = Clip(remaining, a, b);
            }
            return pieces;
        }
    }

    /// <summary>A flat junction made from its actual approach mouths, including its pavement ring.</summary>
    public sealed class CityRoadJunction
    {
        internal CityRoadJunction(Vector2Int node, Vector2 center, RoadEdge ownerEdge,
            IReadOnlyList<Vector2[]> road, IReadOnlyList<Vector2[]> sidewalk, IReadOnlyList<CityRoadPath> sidewalkPaths)
        {
            Node = node; Center = center; OwnerEdge = ownerEdge;
            RoadPolygons = road; SidewalkPolygons = sidewalk; SidewalkPaths = sidewalkPaths;
        }
        public const float MouthInset = 6f;
        public Vector2Int Node { get; }
        public Vector2 Center { get; }
        public RoadEdge OwnerEdge { get; }
        public IReadOnlyList<Vector2[]> RoadPolygons { get; }
        public IReadOnlyList<Vector2[]> SidewalkPolygons { get; }
        public IReadOnlyList<CityRoadPath> SidewalkPaths { get; }
    }

    /// <summary>The road graph retains stable IDs; all physical consumers use these world-XZ paths.</summary>
    public sealed class CityRoadGeometryPlan
    {
        private readonly Dictionary<RoadEdge, CityRoadPath> paths;
        private readonly Dictionary<RoadEdge, IReadOnlyList<Vector2[]>> corridors = new Dictionary<RoadEdge, IReadOnlyList<Vector2[]>>();
        private readonly Dictionary<Vector2Int, IReadOnlyList<Vector2[]>> ground = new Dictionary<Vector2Int, IReadOnlyList<Vector2[]>>();
        private readonly HashSet<Vector2Int> affected = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> replanned = new HashSet<Vector2Int>();
        private readonly CitySpatialPlan spatial;
        private readonly Vector2 origin;
        private readonly float width;
        private CityRoadGeometryPlan(CitySpatialPlan spatial, Vector3 origin, float width,
            IEnumerable<RoadEdge> roads, bool pilot, CityBlueprint blueprint = null,
            ISet<RoadEdge> preservedEdges = null)
        {
            this.spatial = spatial; this.origin = new Vector2(origin.x, origin.z); this.width = width;
            paths = new Dictionary<RoadEdge, CityRoadPath>();
            var curves = new List<RoadEdge>();
            var districtCurves = new List<RoadEdge>();
            int northernServiceRow = int.MinValue;
            if (blueprint != null)
                foreach (CityBlueprintCell cell in blueprint.Cells)
                    if (cell.Topology == CityCellTopologyKind.BuildableLand &&
                        cell.Area.Feature == CityAreaFeatureKind.UrbanDistrict)
                        northernServiceRow = Mathf.Max(northernServiceRow, cell.Cell.y);
            foreach (RoadEdge edge in roads)
            {
                Vector2 start = Node(edge.A), end = Node(edge.B);
                float bulge = pilot ? PilotBulge(edge) : 0;
                bool courtyardStreet = bulge != 0;
                if (!courtyardStreet && blueprint != null && preservedEdges?.Contains(edge) != true)
                    bulge = DistrictBulge(blueprint, edge, northernServiceRow);
                if (bulge == 0) paths.Add(edge, new CityRoadPath(new[] { start, end }));
                else
                {
                    bool obliqueBranch = edge.Equals(PilotEdges[1]);
                    if (obliqueBranch)
                    {
                        paths.Add(edge, CreateObliqueBranch(start, end, width));
                    }
                    else paths.Add(edge, CreateBend(start, end, width, bulge));
                    curves.Add(edge);
                    Vector2Int opposite = edge.A + (edge.IsHorizontal ? Vector2Int.down : Vector2Int.left);
                    replanned.Add(edge.A); replanned.Add(opposite);
                    if (courtyardStreet) { affected.Add(edge.A); affected.Add(opposite); }
                    else districtCurves.Add(edge);
                }
            }
            curves.Sort(RoadEdge.Compare);
            CurvedEdges = new ReadOnlyCollection<RoadEdge>(curves);
            districtCurves.Sort(RoadEdge.Compare);
            ReplannedEdges = new ReadOnlyCollection<RoadEdge>(districtCurves);
            var cells = new List<Vector2Int>(replanned);
            cells.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            ReplannedCells = new ReadOnlyCollection<Vector2Int>(cells);
            if (pilot) ObliqueJunction = CreateJunction();
        }
        private static CityRoadPath CreateBend(Vector2 start, Vector2 end, float roadWidth, float bulge)
        {
            Vector2 tangent = (end - start).normalized;
            Vector2 right = new Vector2(tangent.y, -tangent.x);
            float length = Vector2.Distance(start, end);
            // Keep the node datum knot and the six-metre vehicle approaches.
            // Only the local street between those mouths changes shape.
            var vertices = new List<Vector2> { start, start + tangent * (roadWidth * .5f),
                start + tangent * 6f, start + tangent * 6.5f };
            int steps = Mathf.CeilToInt((length - 13f) / 1.5f);
            for (int i = 1; i < steps; i++)
            {
                float t = (float)i / steps;
                float wave = Mathf.Sin(Mathf.PI * t);
                vertices.Add(start + tangent * (6.5f + (length - 13f) * t) + right * (bulge * wave * wave));
            }
            vertices.Add(end - tangent * 6.5f); vertices.Add(end - tangent * 6f);
            vertices.Add(end - tangent * (roadWidth * .5f)); vertices.Add(end);
            return new CityRoadPath(vertices);
        }
        private static float DistrictBulge(CityBlueprint blueprint, RoadEdge edge, int northernServiceRow)
        {
            Vector2Int first = edge.A;
            Vector2Int second = first + (edge.IsHorizontal ? Vector2Int.down : Vector2Int.left);
            if (!blueprint.TryGetCell(first, out CityBlueprintCell a) ||
                !blueprint.TryGetCell(second, out CityBlueprintCell b) ||
                a.Topology != CityCellTopologyKind.BuildableLand ||
                b.Topology != CityCellTopologyKind.BuildableLand ||
                a.Area.Archetype != b.Area.Archetype) return 0f;
            // Main avenues, river flanks, waterfront and district seams keep
            // their transport datums. Existing courts keep their measured kit.
            if (edge.IsHorizontal && edge.A.y == blueprint.CenterNode.y ||
                edge.IsVertical && blueprint.River != null &&
                (edge.A.x == blueprint.River.CorridorCellX ||
                 edge.A.x == blueprint.River.CorridorCellX + 1) ||
                IsRetainedCourtCell(first) || IsRetainedCourtCell(second)) return 0f;
            // The shore access starts between nodes, with a fixed westward
            // departure. Keep a parallel service street and its short coast
            // connectors straight so cargo can reach a real turning apron
            // before entering the narrower local quarter network.
            if (first.y == northernServiceRow || second.y == northernServiceRow) return 0f;
            switch (a.Area.Archetype)
            {
                case CityDistrictKind.OldTown:
                    // Terrace streets lean uphill; transverse streets lean
                    // towards the shore. Paired runs share a direction and
                    // metre envelope instead of each choosing random noise.
                    return edge.IsHorizontal ? -(1.55f + (edge.A.y % 3) * .3f)
                        : -(1.4f + (edge.A.x % 3) * .35f);
                case CityDistrictKind.Industrial:
                    // The northern service quarters meet OldTown gradually;
                    // the long southern cargo spines remain straight.
                    if (first.y < blueprint.CenterNode.y - 3 || second.y < blueprint.CenterNode.y - 3)
                        return 0f;
                    return edge.IsHorizontal ? -.9f : -.75f;
                default:
                    return 0f;
            }
        }
        private static bool IsRetainedCourtCell(Vector2Int cell) =>
            (cell.x >= 0 && cell.x <= 1 && cell.y >= 7 && cell.y <= 8) ||
            cell == CityCourtyardBlockPlanner.OffsetPairCell;
        private static CityRoadPath CreateObliqueBranch(Vector2 start, Vector2 end, float roadWidth)
        {
            const float angle = -12f * Mathf.Deg2Rad;
            Vector2 axis = (end - start).normalized;
            Vector2 left = new Vector2(-axis.y, axis.x);
            Vector2 tangent = axis * Mathf.Cos(angle) + left * Mathf.Sin(angle);
            float length = Vector2.Distance(start, end);
            float firstX = 6.5f * Mathf.Cos(angle), firstY = 6.5f * Mathf.Sin(angle);
            float span = length - 6.5f - firstX;
            var vertices = new List<Vector2> { start, start + tangent * (roadWidth * .5f), start + tangent * 6f, start + tangent * 6.5f };
            int steps = Mathf.CeilToInt(span / 1.5f);
            for (int i = 1; i < steps; i++)
            {
                float t = (float)i / steps, t2 = t * t, t3 = t2 * t;
                float y = (2f * t3 - 3f * t2 + 1f) * firstY +
                    (t3 - 2f * t2 + t) * span * Mathf.Tan(angle);
                vertices.Add(start + axis * (firstX + span * t) + left * y);
            }
            vertices.Add(end - axis * 6.5f); vertices.Add(end - axis * 6f);
            vertices.Add(end - axis * (roadWidth * .5f)); vertices.Add(end);
            return new CityRoadPath(vertices);
        }
        private CityRoadJunction CreateJunction()
        {
            Vector2Int node = new Vector2Int(1, 8);
            var outer = new List<Vector2[]>(); var inner = new List<Vector2[]>();
            foreach (RoadEdge edge in paths.Keys)
            {
                if (!edge.Contains(node)) continue;
                CityRoadPath path = Get(edge);
                if (edge.B == node) path = path.Reversed();
                // The first six metres are straight at every mouth.
                CityRoadSample sample = path.SampleDistance(CityRoadJunction.MouthInset);
                var arm = new CityRoadPath(new[] { Node(node), sample.Position });
                outer.AddRange(arm.Ribbon(width));
                inner.AddRange(arm.Ribbon(width - CityStreetSurfacePlanner.SidewalkWidth * 2f));
            }
            var road = new List<Vector2[]>();
            foreach (Vector2[] polygon in outer)
            {
                var pieces = new List<Vector2[]> { polygon };
                foreach (Vector2[] existing in road) pieces = SubtractAll(pieces, existing);
                road.AddRange(pieces);
            }
            var pavement = new List<Vector2[]>(road);
            foreach (Vector2[] polygon in inner) pavement = SubtractAll(pavement, polygon);
            float offset = width * .5f - CityStreetSurfacePlanner.SidewalkWidth * .5f;
            Vector2 center = Node(node);
            CityRoadSample branch = Get(PilotEdges[1]).SampleDistance(6f);
            var walks = new List<CityRoadPath>();
            if (paths.ContainsKey(new RoadEdge(node + Vector2Int.left, node)))
                foreach (int side in new[] { -1, 1 })
                    walks.Add(new CityRoadPath(new[] { center + new Vector2(-offset, side * 6f),
                        center + new Vector2(-offset, side * offset), center + new Vector2(-6f, side * offset) }));
            else walks.Add(new CityRoadPath(new[] { center + new Vector2(-offset, 6f), center + new Vector2(-offset, -6f) }));
            foreach (int side in new[] { -1, 1 })
            {
                Vector2 linePoint = center + branch.Right * (side * offset);
                float along = (center.x + offset - linePoint.x) / branch.Tangent.x;
                Vector2 corner = linePoint + branch.Tangent * along;
                walks.Add(new CityRoadPath(new[] { center + new Vector2(offset, side < 0 ? 6f : -6f),
                    corner, branch.Position + branch.Right * (side * offset) }));
            }
            return new CityRoadJunction(node, center, PilotEdges[1],
                new ReadOnlyCollection<Vector2[]>(road), new ReadOnlyCollection<Vector2[]>(pavement),
                new ReadOnlyCollection<CityRoadPath>(walks));
        }
        private static List<Vector2[]> SubtractAll(IEnumerable<Vector2[]> pieces, Vector2[] cut)
        {
            var result = new List<Vector2[]>();
            foreach (Vector2[] piece in pieces) result.AddRange(CityRoadPolygon.Subtract(piece, cut));
            return result;
        }
        private Vector2 Node(Vector2Int node) => origin + spatial.GetCoordinateWorldOffset(node);
        public IReadOnlyList<RoadEdge> CurvedEdges { get; }
        /// <summary>District streets beyond the original four-cell courtyard.</summary>
        public IReadOnlyList<RoadEdge> ReplannedEdges { get; }
        /// <summary>Physical street neighbours; does not reserve semantic gameplay lots.</summary>
        public IReadOnlyList<Vector2Int> ReplannedCells { get; }
        internal IEnumerable<RoadEdge> Edges => paths.Keys;
        public CityRoadJunction ObliqueJunction { get; }
        public float GetEndpointInset(RoadEdge edge, Vector2Int node) =>
            ObliqueJunction != null && node == ObliqueJunction.Node ? CityRoadJunction.MouthInset : width * .5f;
        public CityRoadPath Get(RoadEdge edge) => paths[edge];
        public IReadOnlyList<Vector2[]> GetCorridor(RoadEdge edge)
        {
            if (!corridors.TryGetValue(edge, out IReadOnlyList<Vector2[]> polygons))
            { polygons = Get(edge).Ribbon(width); corridors.Add(edge, polygons); }
            return polygons;
        }
        public bool ContainsRoad(RoadEdge edge, Vector2 point)
        {
            if (ObliqueJunction != null && edge.Contains(ObliqueJunction.Node) &&
                ContainsJunction(point)) return true;
            foreach (Vector2[] polygon in GetCorridor(edge))
                if (CityRoadPolygon.Contains(polygon, point)) return true;
            CityRoadPath path = Get(edge);
            foreach (Vector2 endpoint in new[] { path.Vertices[0], path.Vertices[path.Vertices.Count - 1] })
                if (Mathf.Abs(point.x - endpoint.x) <= width * .5f + .001f &&
                    Mathf.Abs(point.y - endpoint.y) <= width * .5f + .001f) return true;
            return false;
        }
        public bool ContainsJunction(Vector2 point)
        {
            if (ObliqueJunction == null) return false;
            foreach (Vector2[] polygon in ObliqueJunction.RoadPolygons)
                if (CityRoadPolygon.Contains(polygon, point)) return true;
            return false;
        }
        public bool IsCurved(RoadEdge edge) => paths.TryGetValue(edge, out CityRoadPath path) && !path.IsStraight;
        public bool IsAffectedCell(Vector2Int cell) => affected.Contains(cell);
        public bool IsReplannedCell(Vector2Int cell) => replanned.Contains(cell);
        internal static bool SupportsPilot(CityGenerationSettings settings)
        {
            if (settings.Blueprint?.Id != CityBlueprintCatalog.DefaultBlueprintId ||
                settings.SpatialPlan == null || settings.SpatialPlan.IsUniform ||
                settings.Blueprint.River?.CorridorCellX != 6) return false;
            foreach (Vector2Int cell in new[] { new Vector2Int(0, 7), new Vector2Int(1, 7),
                new Vector2Int(0, 8), new Vector2Int(1, 8) })
                if (!settings.CreatesLot(cell) || settings.IsParkCell(cell)) return false;
            return true;
        }
        internal static void EnsurePilotRoads(CityGenerationSettings settings, List<RoadEdge> roads)
        {
            if (!SupportsPilot(settings)) return;
            foreach (RoadEdge edge in PilotEdges) if (!roads.Contains(edge)) roads.Add(edge);
        }
        public static IReadOnlyList<RoadEdge> PilotEdges { get; } = new ReadOnlyCollection<RoadEdge>(new[] {
            new RoadEdge(new Vector2Int(1, 7), new Vector2Int(1, 8)),
            new RoadEdge(new Vector2Int(1, 8), new Vector2Int(2, 8)),
            new RoadEdge(new Vector2Int(1, 8), new Vector2Int(1, 9)) });
        private static float PilotBulge(RoadEdge edge)
        {
            for (int i = 0; i < PilotEdges.Count; i++) if (edge.Equals(PilotEdges[i])) return i == 1 ? 2f : i == 2 ? -.9f : .9f;
            return 0;
        }
        internal static CityRoadGeometryPlan Create(CityGenerationSettings settings, Vector3 origin, IList<RoadEdge> roads,
            ISet<RoadEdge> preservedEdges = null, bool replanDistricts = true) =>
            new CityRoadGeometryPlan(settings.SpatialPlan, origin, settings.RoadWidth, roads, SupportsPilot(settings),
                replanDistricts && SupportsPilot(settings) ? settings.Blueprint : null, preservedEdges);
        internal static CityRoadGeometryPlan Straight(CitySpatialPlan spatial, Vector3 origin, float width, IEnumerable<RoadEdge> roads) =>
            new CityRoadGeometryPlan(spatial, origin, width, roads, false);
        public IReadOnlyList<Vector2[]> GetGroundPolygons(Vector2Int cell)
        {
            if (ground.TryGetValue(cell, out IReadOnlyList<Vector2[]> cached)) return cached;
            Rect bounds = spatial.GetCellBounds(cell); bounds.position += origin;
            var pieces = new List<Vector2[]> { CityRoadPolygon.Rectangle(bounds) };
            var cuts = new List<Vector2[]>();
            foreach (KeyValuePair<RoadEdge, CityRoadPath> entry in paths)
            {
                CityRoadPath path = entry.Value;
                Rect reach = path.Bounds; reach.xMin -= width * .5f; reach.xMax += width * .5f;
                reach.yMin -= width * .5f; reach.yMax += width * .5f;
                if (!bounds.Overlaps(reach)) continue;
                cuts.AddRange(path.Ribbon(width));
                foreach (Vector2Int node in new[] { entry.Key.A, entry.Key.B })
                    if (ObliqueJunction != null && node == ObliqueJunction.Node) cuts.AddRange(ObliqueJunction.RoadPolygons);
                    else cuts.Add(CityRoadPolygon.Rectangle(new Rect(Node(node) - Vector2.one * width * .5f, Vector2.one * width)));
            }
            foreach (Vector2[] cut in cuts)
            {
                var remaining = new List<Vector2[]>();
                Rect cutBounds = CityRoadPolygon.Bounds(cut);
                foreach (Vector2[] piece in pieces)
                    if (CityRoadPolygon.Bounds(piece).Overlaps(cutBounds)) remaining.AddRange(CityRoadPolygon.Subtract(piece, cut));
                    else remaining.Add(piece);
                pieces = remaining;
            }
            cached = new ReadOnlyCollection<Vector2[]>(pieces); ground.Add(cell, cached); return cached;
        }
    }
}
