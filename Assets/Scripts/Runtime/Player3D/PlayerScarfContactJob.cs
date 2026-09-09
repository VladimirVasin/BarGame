using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BarPromenade
{
    // The same sequential contact projections run as one native job. No
    // parallel reductions or fast float transformations change their order.
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    internal struct PlayerScarfContactJob : IJob
    {
        [ReadOnly] public NativeArray<Vector3> Previous;
        public NativeArray<Vector3> Current;
        [ReadOnly] public NativeArray<int> Indices;
        [ReadOnly] public NativeArray<PlayerScarfCollisionSnapshot.Triangle> Triangles;
        [ReadOnly] public NativeArray<PlayerScarfCollisionSnapshot.Node> Nodes;
        [ReadOnly] public NativeArray<int> Orders;
        [ReadOnly] public NativeArray<PlayerScarfCollisionSnapshot.Component> Components;
        public NativeArray<int> candidates, closedCandidates, componentCandidates, stack, Results;
        public NativeArray<Vector3> checkedPoints, checkedA, checkedB, checkedC;
        public NativeArray<bool> stablePoints, stableFaces;
        public int RootGlobal, RootComponents, IterationLimit, VertexCount, IndexCount;
        public float Thickness;
        private int LastContactCount, LastPassCount;

        public void Execute()
        {
            int nativeExecution = 1;
            MarkManagedExecution(ref nativeExecution);
            Results[2] = nativeExecution;
            LastContactCount = LastPassCount = 0;
            for (int pass = 0; pass < IterationLimit; pass++)
            {
                LastPassCount++;
                int before = LastContactCount;
                for (int vertex = 0; vertex < VertexCount; vertex++)
                {
                    Vector3 position = Current[vertex];
                    if (pass != 0 && stablePoints[vertex] && checkedPoints[vertex].Equals(position)) continue;
                    int contactsBeforePoint = LastContactCount;
                    Vector3 corrected = position;
                    ProjectPoint(Previous[vertex], ref corrected, Thickness, pass == 0);
                    Current[vertex] = corrected;
                    // Sweep and closed recovery can cancel each other on pass0.
                    stablePoints[vertex] = position.Equals(corrected) &&
                        (pass != 0 || LastContactCount == contactsBeforePoint);
                    checkedPoints[vertex] = corrected;
                }
                ProjectFaces(Previous, Current, Indices, Thickness, pass == 0);
                if (LastContactCount == before) break;
            }
            Results[0] = LastContactCount;
            Results[1] = LastPassCount;
        }

        [BurstDiscard]
        private static void MarkManagedExecution(ref int nativeExecution) => nativeExecution = 0;

        private int Query(Bounds bounds)
        {
            int count = 0, pending = 0;
            if (RootGlobal >= 0) stack[pending++] = RootGlobal;
            while (pending > 0)
            {
                var node = Nodes[stack[--pending]];
                if (!node.Bounds.Intersects(bounds)) continue;
                if (node.Count != 0)
                {
                    for (int i = node.Start; i < node.Start + node.Count; i++)
                    {
                        int index = Orders[i];
                        if (Triangles[index].Bounds.Intersects(bounds)) candidates[count++] = index;
                    }
                }
                else { stack[pending++] = node.Right; stack[pending++] = node.Left; }
            }
            return count;
        }

        private int QueryClosed(Vector3 point)
        {
            int componentCount = 0, pending = 0;
            if (RootComponents >= 0) stack[pending++] = RootComponents;
            while (pending > 0)
            {
                var node = Nodes[stack[--pending]];
                if (!BoundsContainsPoint(node.Bounds, point)) continue;
                if (node.Count != 0)
                {
                    for (int i = node.Start; i < node.Start + node.Count; i++)
                    {
                        int index = Orders[i];
                        if (BoundsContainsPoint(Components[index].Bounds, point)) componentCandidates[componentCount++] = index;
                    }
                }
                else { stack[pending++] = node.Right; stack[pending++] = node.Left; }
            }
            // Keep the managed snapshot's ordinal recovery order.
            for (int i = 1; i < componentCount; i++)
            {
                int value = componentCandidates[i], at = i;
                while (at > 0 && componentCandidates[at - 1] > value)
                { componentCandidates[at] = componentCandidates[at - 1]; at--; }
                componentCandidates[at] = value;
            }
            int count = 0;
            for (int i = 0; i < componentCount; i++)
            {
                int nearest = Nearest(Components[componentCandidates[i]].Root, point);
                if (nearest >= 0) closedCandidates[count++] = nearest;
            }
            return count;
        }

        private int Nearest(int root, Vector3 point)
        {
            int nearest = -1, pending = 0;
            float squared = float.PositiveInfinity;
            if (root >= 0) stack[pending++] = root;
            while (pending > 0)
            {
                var node = Nodes[stack[--pending]];
                if (BoundsSquaredDistance(node.Bounds, point) > squared) continue;
                if (node.Count != 0)
                {
                    for (int i = node.Start; i < node.Start + node.Count; i++)
                    {
                        int index = Orders[i];
                        var triangle = Triangles[index];
                        if (BoundsSquaredDistance(triangle.Bounds, point) > squared) continue;
                        float distance = (ClosestPoint(point, triangle.A, triangle.B, triangle.C) - point).sqrMagnitude;
                        if (distance < squared) { squared = distance; nearest = index; }
                    }
                }
                else
                {
                    float left = BoundsSquaredDistance(Nodes[node.Left].Bounds, point);
                    float right = BoundsSquaredDistance(Nodes[node.Right].Bounds, point);
                    stack[pending++] = left <= right ? node.Right : node.Left;
                    stack[pending++] = left <= right ? node.Left : node.Right;
                }
            }
            return nearest;
        }

        private static bool BoundsContainsPoint(Bounds bounds, Vector3 point)
        {
            Vector3 delta = point - bounds.center, extents = bounds.extents;
            return Mathf.Abs(delta.x) <= extents.x && Mathf.Abs(delta.y) <= extents.y && Mathf.Abs(delta.z) <= extents.z;
        }

        private static float BoundsSquaredDistance(Bounds bounds, Vector3 point)
        {
            Vector3 delta = point - bounds.center, extents = bounds.extents;
            float x = Mathf.Max(0f, Mathf.Abs(delta.x) - extents.x);
            float y = Mathf.Max(0f, Mathf.Abs(delta.y) - extents.y);
            float z = Mathf.Max(0f, Mathf.Abs(delta.z) - extents.z);
            return x*x + y*y + z*z;
        }

        private void ProjectPoint(Vector3 previous, ref Vector3 point,
            float margin, bool sweep)
        {
            if (sweep)
            {
                float marginSquared = margin * margin;
                Bounds swept = new Bounds(previous, Vector3.zero);
                swept.Encapsulate(point); swept.Expand(margin * 2f);
                int candidateCount = Query(swept);
                for (int index = 0; index < candidateCount; index++)
                {
                    PlayerScarfCollisionSnapshot.Triangle triangle = Triangles[candidates[index]];
                    Vector3 normal = triangle.Normal;
                    if (normal.sqrMagnitude < .5f) continue;
                    Vector3 closest = ClosestPoint(point, triangle.A, triangle.B, triangle.C);
                    float squared = (point - closest).sqrMagnitude;
                    // Follow the local feature, rather than the triangle centroid:
                    // a turning body has different motion at each point on a face.
                    Vector3 featureWeights = Barycentric(closest, triangle.A, triangle.B, triangle.C);
                    Vector3 delta = (triangle.A - triangle.PreviousA) * featureWeights.x +
                        (triangle.B - triangle.PreviousB) * featureWeights.y +
                        (triangle.C - triangle.PreviousC) * featureWeights.z;
                    Vector3 start = previous + delta;
                    if (SegmentTriangle(start, point, triangle.A, triangle.B, triangle.C, out Vector3 hit))
                    {
                        Vector3 side = triangle.ClosedSurface || triangle.OneSided ? normal :
                            (Vector3.Dot(start - triangle.A, normal) >= 0f ? normal : -normal);
                        point = hit + side * margin;
                        LastContactCount++;
                    }
                    // A small deadband exceeds float quantisation at the village's
                    // world coordinates, so a resolved 3 mm contact can settle.
                    else if (squared < marginSquared * .95f)
                    {
                        Vector3 side = squared > 1e-12f ? (point - closest).normalized :
                            (Vector3.Dot(previous - triangle.PreviousA, triangle.PreviousNormal) >= 0f ? normal : -normal);
                        if ((triangle.ClosedSurface || triangle.OneSided) && Vector3.Dot(side, normal) < 0f) side = normal;
                        point = closest + side * margin;
                        LastContactCount++;
                    }
                }
            }
            // Recovery also covers equipping beside an object and a newly visible
            // obstacle. This is per connected closed surface, not a scene AABB.
            // A contact can move the point to a different nearest face. Never
            // classify it with the closest point captured before that movement:
            // stale faces pulled already separated scarf faces back into corners.
            int closedCount = QueryClosed(point);
            for (int closed = 0; closed < closedCount; closed++)
            {
                var contact = Triangles[closedCandidates[closed]];
                Vector3 closest = ClosestPoint(point, contact.A, contact.B, contact.C);
                float signed = Vector3.Dot(point - closest, contact.Normal);
                if (signed >= -.00005f) continue;
                point = closest + contact.Normal * margin;
                LastContactCount++;
                // Another overlapping solid needs a fresh nearest-face query
                // from this new position on the next solver pass.
                break;
            }
        }

        private void ProjectFaces(NativeArray<Vector3> previous, NativeArray<Vector3> vertices, NativeArray<int> indices,
            float margin, bool firstPass)
        {
            for (int face = 0; face < IndexCount; face += 3)
            {
                int ia = indices[face], ib = indices[face + 1], ic = indices[face + 2];
                int slot = face / 3;
                Vector3 startA = vertices[ia], startB = vertices[ib], startC = vertices[ic];
                if (!firstPass && stableFaces[slot] && checkedA[slot].Equals(startA) &&
                    checkedB[slot].Equals(startB) && checkedC[slot].Equals(startC)) continue;
                Bounds bounds = new Bounds(vertices[ia], Vector3.zero);
                bounds.Encapsulate(vertices[ib]); bounds.Encapsulate(vertices[ic]); bounds.Expand(margin * 2f);
                int candidateCount = Query(bounds);
                for (int obstacle = 0; obstacle < candidateCount; obstacle++)
                {
                    var triangle = Triangles[candidates[obstacle]];
                    Vector3 a = vertices[ia], b = vertices[ib], c = vertices[ic];
                    if (!bounds.Intersects(triangle.Bounds)) continue;
                    // AABB overlap alone includes many disjoint slanted faces.
                    // Either supporting plane can prove a separation larger
                    // than the contact thickness before finite-feature work.
                    if (SeparatedByPlane(a, b, c, triangle.A, triangle.Normal, margin) ||
                        SeparatedByPlane(triangle.A, triangle.B, triangle.C, a,
                            Vector3.Cross(b - a, c - a), margin)) continue;
                    if (!Intersects(a, b, c, triangle.A, triangle.B, triangle.C) &&
                        !NearFaces(a, b, c, triangle.A, triangle.B, triangle.C, margin * .95f)) continue;
                    ProjectFiniteFeatures(previous, vertices, ia, ib, ic, triangle, margin);
                    bounds = new Bounds(vertices[ia], Vector3.zero);
                    bounds.Encapsulate(vertices[ib]); bounds.Encapsulate(vertices[ic]); bounds.Expand(margin * 2f);
                }
                stableFaces[slot] = startA.Equals(vertices[ia]) && startB.Equals(vertices[ib]) && startC.Equals(vertices[ic]);
                checkedA[slot] = vertices[ia]; checkedB[slot] = vertices[ib]; checkedC[slot] = vertices[ic];
            }
        }

        private static bool Intersects(Vector3 a, Vector3 b, Vector3 c, Vector3 p, Vector3 q, Vector3 r) =>
            SegmentTriangle(a, b, p, q, r, out _) || SegmentTriangle(b, c, p, q, r, out _) ||
            SegmentTriangle(c, a, p, q, r, out _) || SegmentTriangle(p, q, a, b, c, out _) ||
            SegmentTriangle(q, r, a, b, c, out _) || SegmentTriangle(r, p, a, b, c, out _);

        private static bool SeparatedByPlane(Vector3 a, Vector3 b, Vector3 c,
            Vector3 origin, Vector3 normal, float margin)
        {
            float x = Vector3.Dot(a - origin, normal);
            float y = Vector3.Dot(b - origin, normal);
            float z = Vector3.Dot(c - origin, normal);
            float closest = x > 0f && y > 0f && z > 0f ? Mathf.Min(x, Mathf.Min(y, z)) :
                x < 0f && y < 0f && z < 0f ? Mathf.Max(x, Mathf.Max(y, z)) : 0f;
            // Unnormalized plane distances avoid a square root. Strict > and
            // a tiny outward cushion retain boundary contacts at float precision.
            float clearance = margin + .00001f;
            return closest * closest > clearance * clearance * normal.sqrMagnitude;
        }

        private void ProjectFiniteFeatures(NativeArray<Vector3> previous, NativeArray<Vector3> vertices,
            int ia, int ib, int ic, in PlayerScarfCollisionSnapshot.Triangle obstacle, float margin)
        {
            // A crossing has zero closest-point distance and no geometric
            // separation direction. Resolve its actual intersection point first,
            // on either triangle's edges, using the prior/outward surface side.
            for (int direction = 0; direction < 2; ++direction)
            {
                for (int edge = 0; edge < 3; ++edge)
                {
                    Vector3 a = vertices[ia], b = vertices[ib], c = vertices[ic];
                    Vector3 start = direction == 0 ? Corner(edge, a, b, c) :
                        Corner(edge, obstacle.A, obstacle.B, obstacle.C);
                    Vector3 end = direction == 0 ? Corner((edge + 1) % 3, a, b, c) :
                        Corner((edge + 1) % 3, obstacle.A, obstacle.B, obstacle.C);
                    Vector3 hit;
                    bool crossing = direction == 0
                        ? SegmentTriangle(start, end, obstacle.A, obstacle.B, obstacle.C, out hit)
                        : SegmentTriangle(start, end, a, b, c, out hit);
                    if (!crossing) continue;
                    Vector3 weights;
                    if (direction == 0)
                    {
                        Vector3 along = end - start;
                        float amount = Mathf.Clamp01(Vector3.Dot(hit - start, along) /
                            Mathf.Max(along.sqrMagnitude, 1e-16f));
                        weights = EdgeWeights(edge, amount);
                    }
                    else weights = Barycentric(hit, a, b, c);
                    ProjectFeature(previous, vertices, ia, ib, ic, obstacle, weights, hit, margin, true);
                }
            }

            // All finite closest features: three cloth vertices against the
            // model face, three model vertices against the cloth face, and the
            // nine edge pairs. Recompute after each correction instead of using
            // a stale manifold or pushing unrelated vertices to an infinite plane.
            for (int feature = 0; feature < 15; ++feature)
            {
                Vector3 a = vertices[ia], b = vertices[ib], c = vertices[ic];
                Vector3 point, weights;
                if (feature < 3)
                {
                    Vector3 clothPoint = Corner(feature, a, b, c);
                    point = ClosestPoint(clothPoint, obstacle.A, obstacle.B, obstacle.C);
                    weights = Corner(feature, Vector3.right, Vector3.up, Vector3.forward);
                }
                else if (feature < 6)
                {
                    point = Corner(feature - 3, obstacle.A, obstacle.B, obstacle.C);
                    weights = Barycentric(ClosestPoint(point, a, b, c), a, b, c);
                }
                else
                {
                    int clothEdge = (feature - 6) / 3, modelEdge = (feature - 6) % 3;
                    Vector3 start = Corner(clothEdge, a, b, c);
                    Vector3 end = Corner((clothEdge + 1) % 3, a, b, c);
                    Vector3 otherStart = Corner(modelEdge, obstacle.A, obstacle.B, obstacle.C);
                    Vector3 otherEnd = Corner((modelEdge + 1) % 3, obstacle.A, obstacle.B, obstacle.C);
                    ClosestEdgeParameters(start, end, otherStart, otherEnd, out float clothAmount, out float modelAmount);
                    point = otherStart + (otherEnd - otherStart) * modelAmount;
                    weights = EdgeWeights(clothEdge, clothAmount);
                }
                ProjectFeature(previous, vertices, ia, ib, ic, obstacle, weights, point, margin, false);
            }
        }

        private void ProjectFeature(NativeArray<Vector3> previous, NativeArray<Vector3> vertices,
            int ia, int ib, int ic, in PlayerScarfCollisionSnapshot.Triangle obstacle,
            Vector3 weights, Vector3 modelPoint, float margin, bool crossing)
        {
            Vector3 separation = (vertices[ia] - modelPoint) * weights.x +
                (vertices[ib] - modelPoint) * weights.y + (vertices[ic] - modelPoint) * weights.z;
            float squared = separation.sqrMagnitude;
            if (!crossing && squared >= margin * margin * .95f) return;
            Vector3 normal = obstacle.Normal;
            if (normal.sqrMagnitude < .5f) return;
            bool oneSided = obstacle.ClosedSurface || obstacle.OneSided;
            if (!oneSided)
            {
                Vector3 oldSeparation = (previous[ia] - obstacle.PreviousA) * weights.x +
                    (previous[ib] - obstacle.PreviousA) * weights.y +
                    (previous[ic] - obstacle.PreviousA) * weights.z;
                if (Vector3.Dot(oldSeparation, obstacle.PreviousNormal) < 0f) normal = -normal;
            }
            if (!crossing && squared > 1e-12f)
            {
                Vector3 away = separation / Mathf.Sqrt(squared);
                if (!oneSided || Vector3.Dot(away, normal) >= 0f) normal = away;
            }
            float missing = margin - Vector3.Dot(separation, normal);
            float denominator = weights.sqrMagnitude;
            if (missing <= .000001f || denominator <= 1e-12f) return;
            // Minimum squared vertex movement for the contact's barycentric
            // distance constraint. An edge contact has zero weight on the far
            // vertex; a vertex contact moves that vertex alone.
            Vector3 correction = normal * (missing / denominator);
            vertices[ia] += correction * weights.x;
            vertices[ib] += correction * weights.y;
            vertices[ic] += correction * weights.z;
            LastContactCount++;
        }

        private static Vector3 Corner(int index, Vector3 a, Vector3 b, Vector3 c) =>
            index == 0 ? a : index == 1 ? b : c;

        private static Vector3 EdgeWeights(int edge, float amount) =>
            Corner(edge, Vector3.right, Vector3.up, Vector3.forward) * (1f - amount) +
            Corner((edge + 1) % 3, Vector3.right, Vector3.up, Vector3.forward) * amount;

        private static Vector3 Barycentric(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = point - a;
            float d00 = Vector3.Dot(ab, ab), d01 = Vector3.Dot(ab, ac), d11 = Vector3.Dot(ac, ac);
            float d20 = Vector3.Dot(ap, ab), d21 = Vector3.Dot(ap, ac);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) < 1e-20f)
            {
                float da = (point - a).sqrMagnitude, db = (point - b).sqrMagnitude, dc = (point - c).sqrMagnitude;
                return da <= db && da <= dc ? Vector3.right : db <= dc ? Vector3.up : Vector3.forward;
            }
            float wb = Mathf.Clamp01((d11 * d20 - d01 * d21) / denominator);
            float wc = Mathf.Clamp01((d00 * d21 - d01 * d20) / denominator);
            float wa = Mathf.Clamp01(1f - wb - wc);
            return new Vector3(wa, wb, wc) / Mathf.Max(wa + wb + wc, 1e-12f);
        }

        private static bool NearFaces(Vector3 a, Vector3 b, Vector3 c,
            Vector3 p, Vector3 q, Vector3 r, float margin)
        {
            // Thickness belongs to the complete fabric, not just its vertices.
            // A long cloth edge can otherwise graze a model edge with almost no
            // clearance even when every cloth vertex clears a face by 3 mm.
            float squared = margin * margin;
            return (a - ClosestPoint(a, p, q, r)).sqrMagnitude < squared ||
                (b - ClosestPoint(b, p, q, r)).sqrMagnitude < squared ||
                (c - ClosestPoint(c, p, q, r)).sqrMagnitude < squared ||
                (p - ClosestPoint(p, a, b, c)).sqrMagnitude < squared ||
                (q - ClosestPoint(q, a, b, c)).sqrMagnitude < squared ||
                (r - ClosestPoint(r, a, b, c)).sqrMagnitude < squared ||
                EdgeDistanceSquared(a, b, p, q) < squared || EdgeDistanceSquared(a, b, q, r) < squared ||
                EdgeDistanceSquared(a, b, r, p) < squared || EdgeDistanceSquared(b, c, p, q) < squared ||
                EdgeDistanceSquared(b, c, q, r) < squared || EdgeDistanceSquared(b, c, r, p) < squared ||
                EdgeDistanceSquared(c, a, p, q) < squared || EdgeDistanceSquared(c, a, q, r) < squared ||
                EdgeDistanceSquared(c, a, r, p) < squared;
        }

        private static float EdgeDistanceSquared(Vector3 p, Vector3 q, Vector3 r, Vector3 s)
        {
            ClosestEdgeParameters(p, q, r, s, out float u, out float v);
            return ((p - r) + (q - p) * u - (s - r) * v).sqrMagnitude;
        }

        private static void ClosestEdgeParameters(Vector3 p, Vector3 q, Vector3 r, Vector3 s,
            out float u, out float v)
        {
            Vector3 d1 = q - p, d2 = s - r, offset = p - r;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, offset);
            if (a <= 1e-12f && e <= 1e-12f) { u = v = 0f; return; }
            if (a <= 1e-12f) { u = 0f; v = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, offset);
                if (e <= 1e-12f) { v = 0f; u = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denominator = a * e - b * b;
                    u = denominator > 1e-20f ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
                    v = (b * u + f) / e;
                    if (v < 0f) { v = 0f; u = Mathf.Clamp01(-c / a); }
                    else if (v > 1f) { v = 1f; u = Mathf.Clamp01((b - c) / a); }
                }
            }
        }

        public static bool SegmentTriangle(Vector3 start, Vector3 end, Vector3 a, Vector3 b, Vector3 c,
            out Vector3 hit)
        {
            hit = default;
            Vector3 direction = end - start;
            Vector3 edge1 = b - a, edge2 = c - a;
            Vector3 cross = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, cross);
            if (Mathf.Abs(determinant) < 1e-10f) return false;
            float inverse = 1f / determinant;
            Vector3 from = start - a;
            float u = Vector3.Dot(from, cross) * inverse;
            if (u < -1e-5f || u > 1.00001f) return false;
            Vector3 q = Vector3.Cross(from, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < -1e-5f || u + v > 1.00001f) return false;
            float t = Vector3.Dot(edge2, q) * inverse;
            if (t <= .00001f || t >= .99999f) return false;
            hit = start + direction * t;
            return true;
        }

        public static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denominator = va + vb + vc;
            if (Mathf.Abs(denominator) < 1e-16f) return a;
            return a + ab * (vb / denominator) + ac * (vc / denominator);
        }
    }
}
