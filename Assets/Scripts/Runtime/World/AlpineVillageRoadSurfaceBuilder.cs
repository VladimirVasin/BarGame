using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Paints the former road onto the existing ground triangles. Splitting at
    /// the road outline preserves each terrain plane, with no raised skin or
    /// second collision surface. Successive footprints consume only unpainted
    /// ground, so bends and the warehouse junction never overlap themselves.
    /// </summary>
    internal static class AlpineVillageRoadSurfaceBuilder
    {
        private const int BendSides = 32;
        private const float PathJoinReach = 1.5f;
        internal static readonly Color AsphaltTint = new Color(.24f, .255f, .255f, 1f);
        internal static readonly Color JunctionSoilTint = new Color(.330f, .325f, .295f, 1f);

        /// <summary>Soil ends at the asphalt outline, settling onto the same
        /// terrain plane there instead of placing its raised round cap on top.</summary>
        internal static void FitPathJunctions(Mesh mesh, AlpineVillagePlan plan)
        {
            Bounds meshBounds = mesh.bounds;
            Rect extent = Rect.MinMaxRect(meshBounds.min.x - PathJoinReach,
                meshBounds.min.z - PathJoinReach, meshBounds.max.x + PathJoinReach,
                meshBounds.max.z + PathJoinReach);
            List<Footprint> footprints = CreateFootprints(plan.Expansion);
            foreach (AlpineVillageJunctionPlan junction in plan.Expansion.Junctions)
                footprints.Add(new Footprint(junction.OwnershipContour));
            footprints.RemoveAll(footprint => !extent.Overlaps(footprint.Bounds));
            if (footprints.Count == 0) return;

            Vector3[] source = mesh.vertices;
            Vector3[] sourceNormals = mesh.normals;
            Vector2[] sourceUvs = mesh.uv;
            int[] triangles = mesh.triangles;
            var positions = new List<Vector3>(source.Length);
            var uvs = new List<Vector2>(sourceUvs);
            foreach (Vector3 point in source) positions.Add(Fit(point));
            var result = new List<int>(triangles.Length);
            for (int index = 0; index < triangles.Length; index += 3)
            {
                int a = triangles[index], b = triangles[index + 1], c = triangles[index + 2];
                Rect bounds = Bounds(XZ(source[a]), XZ(source[b]), XZ(source[c]));
                List<List<Vertex>> remaining = null;
                foreach (Footprint footprint in footprints)
                {
                    if (!bounds.Overlaps(footprint.Bounds)) continue;
                    if (remaining == null)
                        remaining = new List<List<Vertex>> { new List<Vertex>
                        {
                            new Vertex(source[a], sourceNormals[a], sourceUvs[a]),
                            new Vertex(source[b], sourceNormals[b], sourceUvs[b]),
                            new Vertex(source[c], sourceNormals[c], sourceUvs[c])
                        }};
                    var outside = new List<List<Vertex>>();
                    foreach (List<Vertex> polygon in remaining)
                    {
                        if (!HasPositiveOverlap(polygon, footprint))
                        {
                            outside.Add(polygon);
                            continue;
                        }
                        List<Vertex> inside = polygon;
                        for (int edge = 0; edge < footprint.Points.Count && inside.Count >= 3; edge++)
                        {
                            Split(inside, footprint.Points[edge],
                                footprint.Points[(edge + 1) % footprint.Points.Count],
                                out List<Vertex> kept, out List<Vertex> rejected);
                            if (rejected.Count >= 3) outside.Add(rejected);
                            inside = kept;
                        }
                    }
                    remaining = outside;
                    if (remaining.Count == 0) break;
                }
                if (remaining == null)
                {
                    result.Add(a); result.Add(b); result.Add(c);
                    continue;
                }
                foreach (List<Vertex> polygon in remaining)
                {
                    int first = positions.Count;
                    foreach (Vertex vertex in polygon)
                    {
                        positions.Add(Fit(vertex.Position));
                        uvs.Add(vertex.Uv);
                    }
                    for (int corner = 1; corner + 1 < polygon.Count; corner++)
                    {
                        if (Math.Abs(Cross(XZ(polygon[corner].Position - polygon[0].Position),
                            XZ(polygon[corner + 1].Position - polygon[0].Position))) < 1e-9) continue;
                        result.Add(first); result.Add(first + corner); result.Add(first + corner + 1);
                    }
                }
            }
            FitApproachPlanes();
            mesh.indexFormat = positions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(result, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Vector3 Fit(Vector3 point)
            {
                Vector2 at = XZ(point);
                float distance = TaperDistance(at);
                if (distance < PathJoinReach)
                    point.y = Mathf.Lerp(AlpineVillageTerrainSampler.SampleMeshHeight(plan, at),
                        point.y, Mathf.SmoothStep(0f, 1f, distance / PathJoinReach));
                return point;
            }

            float TaperDistance(Vector2 at)
            {
                float distance = PathJoinReach;
                foreach (Footprint footprint in footprints)
                {
                    Vector2 nearest = new Vector2(Mathf.Clamp(at.x, footprint.Bounds.xMin, footprint.Bounds.xMax),
                        Mathf.Clamp(at.y, footprint.Bounds.yMin, footprint.Bounds.yMax));
                    if ((nearest - at).sqrMagnitude >= distance * distance) continue;
                    bool inside = true;
                    float nearestEdge = float.PositiveInfinity;
                    for (int edge = 0; edge < footprint.Points.Count; edge++)
                    {
                        Vector2 a = footprint.Points[edge];
                        Vector2 span = footprint.Points[(edge + 1) % footprint.Points.Count] - a;
                        inside &= Cross(span, at - a) >= 0;
                        float along = Mathf.Clamp01(Vector2.Dot(at - a, span) / span.sqrMagnitude);
                        nearestEdge = Mathf.Min(nearestEdge, (at - a - span * along).magnitude);
                    }
                    distance = Mathf.Min(distance, inside ? 0f : nearestEdge);
                }
                return distance;
            }

            void FitApproachPlanes()
            {
                AlpineVillageTerrainGrid grid = AlpineVillageTerrainGrid.Get(plan);
                var fitted = new List<int>(result.Count);
                for (int index = 0; index < result.Count; index += 3)
                {
                    int ia = result[index], ib = result[index + 1], ic = result[index + 2];
                    Vector3 a = positions[ia], b = positions[ib], c = positions[ic];
                    // Outside the taper, the existing raised path is retained.
                    // Near its ground-level mouth, independently tessellated
                    // faces can cut below the terrain despite correct vertices.
                    if (TaperDistance(XZ(a)) >= PathJoinReach && TaperDistance(XZ(b)) >= PathJoinReach &&
                        TaperDistance(XZ(c)) >= PathJoinReach && TaperDistance(XZ((a + b + c) / 3f)) >= PathJoinReach)
                    {
                        fitted.Add(ia); fitted.Add(ib); fitted.Add(ic);
                        continue;
                    }
                    var lifted = new List<Vertex> { Lifted(ia), Lifted(ib), Lifted(ic) };
                    int firstColumn = grid.FindColumn(Mathf.Min(a.x, b.x, c.x));
                    int lastColumn = grid.FindColumn(Mathf.Max(a.x, b.x, c.x));
                    int firstRow = grid.FindRow(Mathf.Min(a.z, b.z, c.z));
                    int lastRow = grid.FindRow(Mathf.Max(a.z, b.z, c.z));
                    for (int row = firstRow; row <= lastRow; row++)
                    for (int column = firstColumn; column <= lastColumn; column++)
                    {
                        var nearLeft = new Vector2(grid.XCoordinates[column], grid.ZCoordinates[row]);
                        var nearRight = new Vector2(grid.XCoordinates[column + 1], grid.ZCoordinates[row]);
                        var farLeft = new Vector2(grid.XCoordinates[column], grid.ZCoordinates[row + 1]);
                        var farRight = new Vector2(grid.XCoordinates[column + 1], grid.ZCoordinates[row + 1]);
                        // The same near-right/far-left diagonal as the terrain
                        // sampler: every resulting face has one ground plane.
                        AppendCellTriangle(lifted, nearLeft, nearRight, farLeft);
                        AppendCellTriangle(lifted, nearRight, farRight, farLeft);
                    }
                }
                result = fitted;

                Vertex Lifted(int index)
                {
                    Vector3 point = positions[index];
                    // Interpolate lift, not world Y, across a terrain crease.
                    // Nonnegative corner lifts then keep the whole face above
                    // its one terrain plane, including triangle interiors.
                    point.y = Mathf.Max(0f, point.y - AlpineVillageTerrainSampler.SampleMeshHeight(plan, XZ(point)));
                    return new Vertex(point, Vector3.up, uvs[index]);
                }

                void AppendCellTriangle(List<Vertex> triangle, Vector2 a, Vector2 b, Vector2 c)
                {
                    Split(triangle, a, b, out List<Vertex> polygon, out _);
                    if (polygon.Count < 3) return;
                    Split(polygon, b, c, out polygon, out _);
                    if (polygon.Count < 3) return;
                    Split(polygon, c, a, out polygon, out _);
                    if (polygon.Count < 3) return;
                    int first = positions.Count;
                    foreach (Vertex vertex in polygon)
                    {
                        Vector3 point = vertex.Position;
                        point.y = AlpineVillageTerrainSampler.SampleMeshHeight(plan, XZ(point)) + Mathf.Max(0f, point.y);
                        positions.Add(point);
                        uvs.Add(vertex.Uv);
                    }
                    for (int corner = 1; corner + 1 < polygon.Count; corner++)
                    {
                        if (Math.Abs(Cross(XZ(polygon[corner].Position - polygon[0].Position),
                            XZ(polygon[corner + 1].Position - polygon[0].Position))) < 1e-9) continue;
                        fitted.Add(first); fitted.Add(first + corner); fitted.Add(first + corner + 1);
                    }
                }
            }
        }

        internal static void Apply(Mesh ground, AlpineVillagePlan plan, IReadOnlyList<Mesh> paths)
        {
            var groups = new List<FootprintGroup>();
            foreach (AlpineVillageJunctionPlan junction in plan.Expansion.Junctions)
            {
                var parts = new List<Footprint>();
                // One baked painting owns the full node, including the soft
                // asphalt/soil transition. There is no material edge inside it.
                foreach (IReadOnlyList<Vector2> polygon in junction.OuterTriangles)
                    parts.Add(new Footprint(polygon, AlpineVillageWorldBuilder.TerrainJunctionMaterialIndex));
                groups.Add(new FootprintGroup(junction.Bounds, parts));
            }
            // The taper meets the ground at zero lift. Paint its short base
            // with the same soil appearance: equal depth after vertex snapping
            // must not expose a white strip beneath the brown path.
            var soilPorts = new List<AlpineVillageJunctionPort>();
            foreach (AlpineVillageJunctionPlan junction in plan.Expansion.Junctions)
            foreach (AlpineVillageJunctionPort port in junction.Ports)
            {
                if (port.Kind == AlpineVillagePathKind.AbandonedRoad) continue;
                // Cover float-scale slivers at the shared rim too. PS1 snapping
                // can magnify one into a pixel-wide line; the node consumes the
                // inward margin first and snow hides the small lateral margin.
                const float paintMargin = .025f;
                Vector2 start = port.MouthCenter - port.Direction * paintMargin;
                Vector2 end = port.MouthCenter + port.Direction * 2f;
                Vector2 side = new Vector2(-port.Direction.y, port.Direction.x) * (port.HalfWidth + paintMargin);
                var quad = new Footprint(new[] { start - side, end - side, end + side, start + side },
                    AlpineVillageWorldBuilder.TerrainSoilMaterialIndex);
                soilPorts.Add(port);
                groups.Add(new FootprintGroup(quad.Bounds, new List<Footprint> { quad }));
            }
            // The hull only consumes still-unpainted corners. It can extend
            // beyond the node contour across a soil mouth, so apply it after
            // all approach coatings and before the old road rectangles.
            foreach (AlpineVillageJunctionPlan junction in plan.Expansion.Junctions)
                groups.Add(new FootprintGroup(junction.Bounds,
                    new List<Footprint> { new Footprint(junction.OwnershipContour, -1) }));
            var road = CreateFootprints(plan.Expansion);
            groups.Add(new FootprintGroup(plan.TerrainMeshBounds, road));
            var positions = new List<Vector3>(ground.vertices);
            var normals = new List<Vector3>(ground.normals);
            var uvs = new List<Vector2>(ground.uv);
            int sourceSlots = ground.subMeshCount;
            var surfaces = new List<int>[5];
            for (int material = 0; material < surfaces.Length; material++) surfaces[material] = new List<int>();
            float asphaltPitch = MountainRoadSurfaceAppearance.GetRecipe(MountainRoadSurfaceKind.Asphalt).MetersPerTile;
            float soilPitch = MountainRoadSurfaceAppearance.GetRecipe(MountainRoadSurfaceKind.ForestFloor).MetersPerTile;
            var candidates = new List<Footprint>();
            var timer = Application.isBatchMode ? System.Diagnostics.Stopwatch.StartNew() : null;
            long candidateCount = 0, rejectedPairs = 0;
            int maximumRemaining = 0;
            ReportPartition("partition_begin");
            for (int material = 0; material < sourceSlots; material++)
            {
                int[] source = ground.GetTriangles(material);
                List<int> remainingSurface = surfaces[material];
                for (int index = 0; index < source.Length; index += 3)
                {
                    int a = source[index], b = source[index + 1], c = source[index + 2];
                    Rect bounds = Bounds(XZ(positions[a]), XZ(positions[b]), XZ(positions[c]));
                    candidates.Clear();
                    foreach (FootprintGroup group in groups)
                    {
                        if (!bounds.Overlaps(group.Bounds)) continue;
                        foreach (Footprint part in group.Parts)
                            if (bounds.Overlaps(part.Bounds)) candidates.Add(part);
                    }
                    candidateCount += candidates.Count;
                    List<List<Vertex>> remaining = null;
                    foreach (Footprint footprint in candidates)
                    {
                        if (remaining == null)
                            remaining = new List<List<Vertex>> { new List<Vertex>
                            {
                                new Vertex(positions[a], normals[a], uvs[a]),
                                new Vertex(positions[b], normals[b], uvs[b]),
                                new Vertex(positions[c], normals[c], uvs[c])
                            }};
                        var outside = new List<List<Vertex>>();
                        foreach (List<Vertex> polygon in remaining)
                        {
                            // Candidate bounds belong to the original coarse
                            // terrain face. Its residual polygons are much
                            // smaller: do not slice them along the infinite
                            // supporting lines of unrelated footprints.
                            if (!HasPositiveOverlap(polygon, footprint))
                            {
                                rejectedPairs++;
                                outside.Add(polygon);
                                continue;
                            }
                            List<Vertex> inside = polygon;
                            for (int edge = 0; edge < footprint.Points.Count && inside.Count >= 3; edge++)
                            {
                                Split(inside, footprint.Points[edge],
                                    footprint.Points[(edge + 1) % footprint.Points.Count],
                                    out List<Vertex> kept, out List<Vertex> rejected);
                                if (rejected.Count >= 3) outside.Add(rejected);
                                inside = kept;
                            }
                            int targetSlot = footprint.Surface < 0 ? material : footprint.Surface;
                            Append(inside, surfaces[targetSlot], targetSlot);
                        }
                        remaining = outside;
                        maximumRemaining = Mathf.Max(maximumRemaining, remaining.Count);
                        if (remaining.Count == 0) break;
                    }
                    if (remaining == null)
                    {
                        remainingSurface.Add(a);
                        remainingSurface.Add(b);
                        remainingSurface.Add(c);
                    }
                    else
                        foreach (List<Vertex> polygon in remaining)
                            Append(polygon, remainingSurface, material);
                }
            }
            BlendApproachNormals();
            ReportPartition("pre_conform");
            AlpineVillageJunctionSeams.Conform(plan, positions, normals, uvs, surfaces, paths);
            ReportPartition("post_conform");
            ground.indexFormat = positions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            ground.SetVertices(positions);
            ground.SetNormals(normals);
            ground.SetUVs(0, uvs);
            ground.subMeshCount = surfaces.Length;
            for (int material = 0; material < surfaces.Length; material++)
                ground.SetTriangles(surfaces[material], material);
            ground.RecalculateBounds();

            void ReportPartition(string stage)
            {
                if (timer == null) return;
                long indices = 0;
                foreach (List<int> surface in surfaces) indices += surface.Count;
                GameLog.Debug("alpine_village", "road_surface_partition",
                    GameLog.Field("stage", stage), GameLog.Field("elapsed_ms", timer.Elapsed.TotalMilliseconds),
                    GameLog.Field("vertices", positions.Count), GameLog.Field("indices", indices),
                    GameLog.Field("candidate_footprints", candidateCount),
                    GameLog.Field("rejected_polygon_pairs", rejectedPairs),
                    GameLog.Field("max_remaining_polygons", maximumRemaining),
                    GameLog.Field("soil_approaches", soilPorts.Count));
                GameLog.Flush();
            }

            void BlendApproachNormals()
            {
                AlpineVillageTerrainGrid grid = AlpineVillageTerrainGrid.Get(plan);
                foreach (Mesh path in paths)
                {
                    Vector3[] pathPositions = path.vertices, pathNormals = path.normals;
                    bool changed = false;
                    foreach (int vertex in new HashSet<int>(path.triangles))
                    {
                        Vector2 point = XZ(pathPositions[vertex]);
                        foreach (AlpineVillageJunctionPort port in soilPorts)
                        {
                            Vector2 delta = point - port.MouthCenter;
                            float along = Vector2.Dot(delta, port.Direction);
                            float across = Mathf.Abs((float)Cross(port.Direction, delta));
                            const float rimTolerance = .0002f;
                            if (along < -rimTolerance || along > 2f + rimTolerance ||
                                across > port.HalfWidth + rimTolerance) continue;
                            // Clipping duplicates vertices before recalculating
                            // normals. Match the smooth original ground at the
                            // zero-lift mouth, then return to the existing path
                            // normal over the same short approach rectangle.
                            int column = grid.FindColumn(point.x), row = grid.FindRow(point.y);
                            float u = Mathf.InverseLerp(grid.XCoordinates[column], grid.XCoordinates[column + 1], point.x);
                            float v = Mathf.InverseLerp(grid.ZCoordinates[row], grid.ZCoordinates[row + 1], point.y);
                            int nearLeft = row * (grid.Columns + 1) + column;
                            int nearRight = nearLeft + 1, farLeft = nearLeft + grid.Columns + 1, farRight = farLeft + 1;
                            Vector3 groundNormal = u + v <= 1f
                                ? normals[nearLeft] * (1f - u - v) + normals[nearRight] * u + normals[farLeft] * v
                                : normals[farRight] * (u + v - 1f) + normals[nearRight] * (1f - v) + normals[farLeft] * (1f - u);
                            float blend = Mathf.SmoothStep(0f, 1f, along / 2f);
                            pathNormals[vertex] = Vector3.Lerp(groundNormal.normalized, pathNormals[vertex], blend).normalized;
                            changed = true;
                            break;
                        }
                    }
                    if (changed) path.SetNormals(pathNormals);
                }
            }

            void Append(List<Vertex> polygon, List<int> target, int material)
            {
                if (polygon.Count < 3) return;
                int first = positions.Count;
                foreach (Vertex vertex in polygon)
                {
                    positions.Add(vertex.Position);
                    normals.Add(vertex.Normal.normalized);
                    uvs.Add(material == AlpineVillageWorldBuilder.TerrainAsphaltMaterialIndex
                        ? XZ(vertex.Position) / asphaltPitch
                        : material == AlpineVillageWorldBuilder.TerrainJunctionMaterialIndex
                        ? AlpineVillageJunctionAppearance.Uv(plan, XZ(vertex.Position))
                        : material == AlpineVillageWorldBuilder.TerrainSoilMaterialIndex
                        ? XZ(vertex.Position) / soilPitch : vertex.Uv);
                }
                for (int corner = 1; corner + 1 < polygon.Count; corner++)
                {
                    // Intersections on an existing corner can repeat it. They
                    // must not introduce degenerate collider triangles.
                    Vector2 ab = XZ(polygon[corner].Position - polygon[0].Position);
                    Vector2 ac = XZ(polygon[corner + 1].Position - polygon[0].Position);
                    if (Math.Abs(Cross(ab, ac)) < 1e-9) continue;
                    target.Add(first);
                    target.Add(first + corner);
                    target.Add(first + corner + 1);
                }
            }
        }

        private static List<Footprint> CreateFootprints(AlpineVillageExpansionPlan expansion)
        {
            var result = new List<Footprint>();
            AlpineVillagePathDescriptor? previous = null;
            foreach (AlpineVillagePathDescriptor path in expansion.Paths)
            {
                if (path.Kind != AlpineVillagePathKind.AbandonedRoad)
                {
                    previous = null;
                    continue;
                }
                Strip(path.Start, path.End, path.SurfaceHalfWidth);
                if (previous.HasValue && (XZ(previous.Value.End) - XZ(path.Start)).sqrMagnitude < .0001f)
                    Bend(path.Start, path.SurfaceHalfWidth);
                previous = path;
            }
            Strip(expansion.ToWorld(new Vector2(-130f, -52f)), expansion.CliffEdge,
                expansion.RoadWidth * .5f);
            Vector3 first = expansion.FarRoadEdge;
            Vector2 previousDirection = Vector2.zero;
            for (float along = -69f; along >= -107f; along -= 2f)
            {
                Vector3 next = expansion.FarRoadPoint(along);
                Strip(first, next, expansion.RoadWidth * .5f);
                Vector2 direction = (XZ(next) - XZ(first)).normalized;
                if (along < -69f && Math.Abs(Cross(previousDirection, direction)) > .001)
                    Bend(first, expansion.RoadWidth * .5f);
                previousDirection = direction;
                first = next;
            }
            return result;

            void Strip(Vector3 start, Vector3 end, float radius)
            {
                Vector2 a = XZ(start), b = XZ(end), direction = (b - a).normalized;
                Vector2 side = new Vector2(-direction.y, direction.x) * radius;
                result.Add(new Footprint(new List<Vector2> { a - side, b - side, b + side, a + side }));
            }

            void Bend(Vector3 centre, float radius)
            {
                var points = new List<Vector2>(BendSides);
                for (int i = 0; i < BendSides; i++)
                {
                    float angle = i * (2f * Mathf.PI / BendSides);
                    points.Add(XZ(centre) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
                result.Add(new Footprint(points));
            }
        }

        private static bool HasPositiveOverlap(List<Vertex> polygon, Footprint footprint)
        {
            if (polygon.Count < 3) return false;
            Vector2 min = XZ(polygon[0].Position), max = min;
            foreach (Vertex vertex in polygon)
            {
                Vector2 point = XZ(vertex.Position);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            if (!Rect.MinMaxRect(min.x, min.y, max.x, max.y).Overlaps(footprint.Bounds)) return false;
            // Both polygons are convex. Bounds alone still overlap for many
            // adjacent slender triangles; SAT rejects those without creating
            // leftover fragments. Touching edges retain their original face.
            for (int edge = 0; edge < polygon.Count; edge++)
                if (Separated(XZ(polygon[edge].Position), XZ(polygon[(edge + 1) % polygon.Count].Position)))
                    return false;
            for (int edge = 0; edge < footprint.Points.Count; edge++)
                if (Separated(footprint.Points[edge], footprint.Points[(edge + 1) % footprint.Points.Count]))
                    return false;
            return true;

            bool Separated(Vector2 start, Vector2 end)
            {
                Vector2 axis = end - start;
                double length = Math.Sqrt((double)axis.x * axis.x + (double)axis.y * axis.y);
                if (length < 1e-9) return false;
                double firstMin = double.PositiveInfinity, firstMax = double.NegativeInfinity;
                double secondMin = double.PositiveInfinity, secondMax = double.NegativeInfinity;
                foreach (Vertex vertex in polygon)
                {
                    double projection = Cross(axis, XZ(vertex.Position) - start);
                    firstMin = Math.Min(firstMin, projection);
                    firstMax = Math.Max(firstMax, projection);
                }
                foreach (Vector2 point in footprint.Points)
                {
                    double projection = Cross(axis, point - start);
                    secondMin = Math.Min(secondMin, projection);
                    secondMax = Math.Max(secondMax, projection);
                }
                // Ten micrometres is below the mesh's float precision here.
                // Rejected slivers stay in the source surface, never a hole.
                return Math.Min(firstMax, secondMax) - Math.Max(firstMin, secondMin) <= length * .00001d;
            }
        }

        private static void Split(List<Vertex> polygon, Vector2 a, Vector2 b,
            out List<Vertex> inside, out List<Vertex> outside)
        {
            inside = new List<Vertex>(polygon.Count + 1);
            outside = new List<Vertex>(polygon.Count + 1);
            Vector2 edge = b - a;
            Vertex previous = polygon[polygon.Count - 1];
            double previousDistance = Cross(edge, XZ(previous.Position) - a);
            foreach (Vertex current in polygon)
            {
                double distance = Cross(edge, XZ(current.Position) - a);
                if ((previousDistance >= 0) != (distance >= 0))
                {
                    var crossing = Vertex.Lerp(previous, current,
                        (float)(previousDistance / (previousDistance - distance)));
                    inside.Add(crossing);
                    outside.Add(crossing);
                }
                if (distance >= 0) inside.Add(current);
                else outside.Add(current);
                previous = current;
                previousDistance = distance;
            }
        }

        private sealed class Footprint
        {
            public readonly IReadOnlyList<Vector2> Points;
            public readonly Rect Bounds;
            public readonly int Surface;
            public Footprint(IReadOnlyList<Vector2> points,
                int surface = AlpineVillageWorldBuilder.TerrainAsphaltMaterialIndex)
            {
                Points = points;
                Surface = surface;
                Vector2 min = points[0], max = min;
                foreach (Vector2 point in points)
                {
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
                Bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }

        private sealed class FootprintGroup
        {
            public readonly Rect Bounds;
            public readonly List<Footprint> Parts;
            public FootprintGroup(Rect bounds, List<Footprint> parts)
            { Bounds = bounds; Parts = parts; }
        }

        private readonly struct Vertex
        {
            public readonly Vector3 Position, Normal;
            public readonly Vector2 Uv;
            public Vertex(Vector3 position, Vector3 normal, Vector2 uv)
            { Position = position; Normal = normal; Uv = uv; }
            public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex(
                Vector3.LerpUnclamped(a.Position, b.Position, t),
                Vector3.LerpUnclamped(a.Normal, b.Normal, t),
                Vector2.LerpUnclamped(a.Uv, b.Uv, t));
        }

        private static Rect Bounds(Vector2 a, Vector2 b, Vector2 c) => Rect.MinMaxRect(
            Mathf.Min(a.x, b.x, c.x), Mathf.Min(a.y, b.y, c.y),
            Mathf.Max(a.x, b.x, c.x), Mathf.Max(a.y, b.y, c.y));
        private static Vector2 XZ(Vector3 value) => new Vector2(value.x, value.z);
        private static double Cross(Vector2 a, Vector2 b) => (double)a.x * b.y - (double)a.y * b.x;
    }
}
