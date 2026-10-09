using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal sealed partial class CombatHurtboxes
    {
        private BodySurface[] bodySurfaces, preparedBodySurfaces;
        private BodySurface[] querySurfaces;
        private float[] queryEntries;
        private int[] queryOrders;
        private IReadOnlyList<CombatBodyDestruction.Piece> preparedBodyPieces;
        private readonly Dictionary<Transform, Matrix4x4> bodyWorldBones = new Dictionary<Transform, Matrix4x4>();
        internal int BodySurfaceGeometryBuilds { get; private set; }
        internal void PrepareBodySurfaces(IReadOnlyList<CombatBodyDestruction.Piece> pieces)
        {
            if (preparedBodyPieces == pieces) return;
            var prepared = new List<BodySurface>();
            foreach (CombatBodyDestruction.Piece piece in pieces)
                if (piece.Eligible) prepared.Add(new BodySurface(piece));
            preparedBodyPieces = pieces; preparedBodySurfaces = prepared.ToArray();
            querySurfaces = new BodySurface[preparedBodySurfaces.Length];
            queryEntries = new float[preparedBodySurfaces.Length];
            queryOrders = new int[preparedBodySurfaces.Length];
        }
        internal void SetBodySurfaces(IReadOnlyList<CombatBodyDestruction.Piece> pieces)
        {
            if (pieces == null)
            {
                if (preparedBodySurfaces != null)
                    foreach (BodySurface surface in preparedBodySurfaces) surface.ReleaseCapture();
                bodySurfaces = null; bodyWorldBones.Clear(); return;
            }
            PrepareBodySurfaces(pieces);
            bodySurfaces = preparedBodySurfaces;
        }
        private void CaptureBodySurfaces(bool refreshSurfaces)
        {
            if (bodySurfaces == null) return;
            bodyWorldBones.Clear();
            foreach (BodySurface surface in bodySurfaces) surface.Capture(bodyWorldBones, refreshSurfaces);
        }
        private bool SweepBodySurfaces(Vector3 from, Vector3 to, float radius, Vector3 direction, ref Hit hit, float first)
        {
            if (bodySurfaces == null) return false;
            bool found = false; Vector3 delta = to - from;
            var query = new SegmentQuery(from, delta, radius);
            int count = 0, firstSurface = -1;
            // Nearer conservative bounds go first so an exact front surface can
            // exclude hidden meshes before their posed triangles are built.
            for (int order = 0; order < bodySurfaces.Length; order++)
            {
                BodySurface surface = bodySurfaces[order];
                if (!surface.Active || !query.TryIntersect(surface.Bounds, first, out float entry)) continue;
                int slot = count++;
                while (slot > 0 && entry < queryEntries[slot - 1])
                {
                    querySurfaces[slot] = querySurfaces[slot - 1];
                    queryEntries[slot] = queryEntries[slot - 1];
                    queryOrders[slot] = queryOrders[slot - 1];
                    slot--;
                }
                querySurfaces[slot] = surface; queryEntries[slot] = entry; queryOrders[slot] = order;
            }
            for (int candidate = 0; candidate < count; candidate++)
            {
                if (queryEntries[candidate] > first) break;
                BodySurface surface = querySurfaces[candidate];
                int order = queryOrders[candidate];
                if (surface.PrepareQuery(query, first)) BodySurfaceGeometryBuilds++;
                if (!query.Intersects(surface.Bounds, first)) continue;
                for (int triangleIndex = 0; triangleIndex < surface.QueryTriangleCount; triangleIndex++)
                {
                    HeadTriangle triangle = surface.Triangles[triangleIndex];
                    if (!query.Intersects(triangle.Bounds, first) ||
                        !triangle.FirstContact(from, delta, radius, out float fraction) ||
                        fraction > first || (fraction == first && order >= firstSurface)) continue;
                    Vector3 centre = from + delta * fraction, point = triangle.Closest(centre), normal = centre - point;
                    if (normal.sqrMagnitude < .00000001f)
                    {
                        normal = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).normalized;
                        if (Vector3.Dot(normal, direction) > 0f) normal = -normal;
                    }
                    else normal.Normalize();
                    CombatBodyDestruction.Piece piece = surface.Piece;
                    MeleeBodyRegion region = CombatBodyAnatomy.CombatRegion(piece.Region);
                    Vector3 offset = actorFrame.InverseTransformVector(point - actorFrame.position);
                    var location = MeleeHitLocation.FromLocalSurface(region, offset.x, offset.y, offset.z);
                    hit = new Hit(point, normal, direction, fraction, location, CombatBodyAnatomy.ToPart(piece.Region),
                        actorFrame.InverseTransformPoint(point), actorFrame.InverseTransformDirection(direction),
                        piece.Region, piece.Patch, piece.Released != null);
                    first = fraction; firstSurface = order; found = true;
                }
            }
            return found;
        }
        private sealed class BodySurface
        {
            internal readonly CombatBodyDestruction.Piece Piece;
            private readonly HeadSurface skin;
            private readonly int[] indices;
            private readonly Vector3[] points;
            private Matrix4x4 frozenMatrix;
            private Vector3[] frozenVertices;
            private HeadTriangle[] meshTriangles;
            private bool skinned, geometryReady;
            internal HeadTriangle[] Triangles;
            internal int QueryTriangleCount;
            internal Bounds Bounds;
            internal bool Active;
            internal BodySurface(CombatBodyDestruction.Piece piece)
            {
                Piece = piece; skin = new HeadSurface(piece.Skin, true, captureBlendShapes: true,
                    mutableTopology: piece.Torso != null, initialGeometryVersion: piece.GeometryVersion,
                    initialTopologyVersion: piece.Torso?.TopologyBuilds);
                indices = piece.Skin.sharedMesh.triangles;
                points = new Vector3[piece.Skin.sharedMesh.vertexCount];
                Triangles = skin.Triangles;
            }
            internal void Capture(Dictionary<Transform, Matrix4x4> worldBones, bool refreshSurface)
            {
                geometryReady = false;
                if (Piece.Debris) { Active = false; return; }
                if (Piece.Skin.enabled || Piece.QueryOriginal)
                {
                    if (refreshSurface) Piece.RefreshSurface();
                    // Blendshape vertices and bones share the same frozen snapshot;
                    // exact hand triangles can wait for an intersecting contact.
                    skin.CapturePose(worldBones, Piece.GeometryVersion, Piece.QueryOriginal, Piece.Torso?.TopologyBuilds); skinned = true;
                    Active = skin.Active; Bounds = skin.Bounds; return;
                }
                MeshRenderer released = Piece.Released;
                Active = released != null && released.enabled && released.gameObject.activeInHierarchy;
                if (!Active) return;
                // Baked vertices include the last live deformation and exact source scale.
                skinned = false;
                frozenVertices = Piece.BakedVertices;
                frozenMatrix = released.transform.localToWorldMatrix;
                Bounds = TransformBounds(Piece.Baked.bounds, frozenMatrix);
            }
            internal bool PrepareQuery(SegmentQuery query, float maximumFraction)
            {
                bool firstQuery = !geometryReady;
                geometryReady = true;
                if (skinned)
                {
                    skin.PrepareQuery(query, maximumFraction); Bounds = skin.Bounds; Triangles = skin.Triangles;
                    QueryTriangleCount = skin.QueryTriangleCount;
                }
                else
                {
                    if (firstQuery) CaptureMesh();
                    QueryTriangleCount = Triangles.Length;
                }
                return firstQuery;
            }
            private void CaptureMesh()
            {
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = frozenMatrix.MultiplyPoint3x4(frozenVertices[i]);
                    if (i == 0) Bounds = new Bounds(points[i], Vector3.zero); else Bounds.Encapsulate(points[i]);
                }
                meshTriangles ??= new HeadTriangle[indices.Length / 3];
                Triangles = meshTriangles;
                for (int i = 0; i < Triangles.Length; i++)
                    Triangles[i] = new HeadTriangle(points[indices[i * 3]], points[indices[i * 3 + 1]], points[indices[i * 3 + 2]]);
            }
            private static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
            {
                Vector3 e = local.extents;
                Vector3 x = matrix.MultiplyVector(new Vector3(e.x, 0f, 0f));
                Vector3 y = matrix.MultiplyVector(new Vector3(0f, e.y, 0f));
                Vector3 z = matrix.MultiplyVector(new Vector3(0f, 0f, e.z));
                return new Bounds(matrix.MultiplyPoint3x4(local.center), new Vector3(
                    Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                    Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                    Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2f);
            }
            internal void ReleaseCapture()
            {
                Active = skinned = geometryReady = false;
                frozenVertices = null; frozenMatrix = default; Bounds = default;
                Triangles = skin.Triangles;
            }
        }
    }
}
