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
        internal int BodySurfacePoseBuilds { get; private set; }
        internal void PrepareBodySurfaces(IReadOnlyList<CombatBodyDestruction.Piece> pieces)
        {
            if (preparedBodyPieces == pieces) return;
            var prepared = new List<BodySurface>();
            var originals = new Dictionary<SkinnedMeshRenderer, List<CombatBodyDestruction.Piece>>();
            foreach (CombatBodyDestruction.Piece piece in pieces)
            {
                if (!piece.Eligible) continue;
                prepared.Add(new BodySurface(piece));
                if (piece.Flesh || piece.Bone || piece.Source == null) continue;
                if (!originals.TryGetValue(piece.Source, out var partitions))
                { partitions = new List<CombatBodyDestruction.Piece>(); originals.Add(piece.Source, partitions); }
                partitions.Add(piece);
            }
            foreach (var original in originals) prepared.Add(new BodySurface(original.Key, original.Value));
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
            foreach (BodySurface surface in bodySurfaces)
                if (surface.Capture(bodyWorldBones, refreshSurfaces)) BodySurfacePoseBuilds++;
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
                    CombatBodyDestruction.Piece piece = surface.ContactPiece(triangleIndex, point);
                    if (piece == null) continue;
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
            private readonly CombatBodyDestruction.Piece[] originals;
            private readonly bool[] originalActive;
            private readonly HeadTriangle[][] authoredPartitions;
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
            internal BodySurface(SkinnedMeshRenderer source, List<CombatBodyDestruction.Piece> partitions)
            {
                originals = partitions.ToArray(); originalActive = new bool[originals.Length];
                authoredPartitions = new HeadTriangle[originals.Length][];
                skin = new HeadSurface(source, true, captureBlendShapes: true,
                    authoredMesh: originals[0].Deformation?.AuthoredSourceMesh);
                for (int part = 0; part < originals.Length; part++)
                {
                    Mesh mesh = originals[part].Deformation?.AuthoredPartitionMesh ?? originals[part].Skin.sharedMesh;
                    Vector3[] vertices = mesh.vertices; int[] triangles = mesh.triangles;
                    var faces = authoredPartitions[part] = new HeadTriangle[triangles.Length / 3];
                    for (int triangle = 0; triangle < faces.Length; triangle++)
                        faces[triangle] = new HeadTriangle(vertices[triangles[triangle * 3]],
                            vertices[triangles[triangle * 3 + 1]], vertices[triangles[triangle * 3 + 2]]);
                }
                Triangles = skin.Triangles;
            }
            internal CombatBodyDestruction.Piece ContactPiece(int triangleIndex, Vector3 point)
            {
                if (originals == null) return Piece;
                // Classify the exact production contact in authored coordinates.
                // A cut mesh can interpolate weights across a joint or bridge an
                // imported nonplanar polygon; neither is the intact drawn surface.
                Vector3 authored = skin.AuthoredQueryPoint(triangleIndex, point);
                CombatBodyDestruction.Piece result = null;
                float closest = float.PositiveInfinity;
                for (int part = 0; part < originals.Length; part++)
                {
                    if (!originalActive[part]) continue;
                    foreach (HeadTriangle triangle in authoredPartitions[part])
                    {
                        if (triangle.Bounds.SqrDistance(authored) >= closest) continue;
                        float distance = (triangle.Closest(authored) - authored).sqrMagnitude;
                        if (distance >= closest) continue;
                        closest = distance; result = originals[part];
                    }
                }
                return result;
            }
            internal bool Capture(Dictionary<Transform, Matrix4x4> worldBones, bool refreshSurface)
            {
                bool wasActive = Active;
                if (originals != null)
                {
                    bool visible = false, changed = false;
                    for (int part = 0; part < originals.Length; part++)
                    {
                        bool active = originals[part].QueryOriginal && !originals[part].Debris;
                        changed |= originalActive[part] != active; originalActive[part] = active; visible |= active;
                    }
                    if (!visible) { Active = geometryReady = false; return wasActive; }
                    changed |= skin.CapturePose(worldBones, originals[0].Deformation?.SourceGeometryVersion ?? 0u, true);
                    Active = skin.Active; skinned = true;
                    changed |= Active && !wasActive;
                    if (changed) geometryReady = false;
                    Bounds = skin.Bounds; return changed;
                }
                if (Piece.Debris) { Active = geometryReady = false; return wasActive; }
                if (Piece.QueryOriginal) { Active = geometryReady = false; return wasActive; }
                if (Piece.Skin.enabled)
                {
                    if (refreshSurface) Piece.RefreshSurface();
                    // Blendshape vertices and bones share the same frozen snapshot;
                    // exact hand triangles can wait for an intersecting contact.
                    bool captured = skin.CapturePose(worldBones, Piece.GeometryVersion, false, Piece.Torso?.TopologyBuilds);
                    bool changed = captured || skin.Active && (!skinned || !wasActive);
                    skinned = true;
                    if (changed) geometryReady = false;
                    Active = skin.Active; Bounds = skin.Bounds; return changed;
                }
                MeshRenderer released = Piece.Released;
                Active = released != null && released.enabled && released.gameObject.activeInHierarchy;
                if (!Active) { geometryReady = false; return wasActive; }
                // Baked vertices include the last live deformation and exact source scale.
                Matrix4x4 matrix = released.transform.localToWorldMatrix;
                bool moved = skinned || !wasActive || frozenVertices != Piece.BakedVertices || !frozenMatrix.Equals(matrix);
                skinned = false;
                frozenVertices = Piece.BakedVertices;
                frozenMatrix = matrix;
                if (moved) { geometryReady = false; Bounds = TransformBounds(Piece.Baked.bounds, frozenMatrix); }
                return moved;
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
