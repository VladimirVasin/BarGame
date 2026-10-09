using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal sealed partial class CombatHurtboxes
    {
        private BodySurface[] bodySurfaces;
        internal void SetBodySurfaces(IReadOnlyList<CombatBodyDestruction.Piece> pieces)
        {
            if (bodySurfaces != null) foreach (BodySurface surface in bodySurfaces) surface.Dispose();
            if (pieces == null) { bodySurfaces = null; return; }
            bodySurfaces = new BodySurface[pieces.Count];
            for (int i = 0; i < pieces.Count; i++) bodySurfaces[i] = new BodySurface(pieces[i]);
        }
        private void CaptureBodySurfaces()
        {
            if (bodySurfaces == null) return;
            foreach (BodySurface surface in bodySurfaces) surface.Capture();
        }
        private bool SweepBodySurfaces(Vector3 from, Vector3 to, float radius, Vector3 direction, ref Hit hit, float first)
        {
            if (bodySurfaces == null) return false;
            bool found = false; Vector3 delta = to - from;
            foreach (BodySurface surface in bodySurfaces)
            {
                if (!surface.Active || !IntersectsSegment(surface.Bounds, from, delta, radius)) continue;
                foreach (HeadTriangle triangle in surface.Triangles)
                {
                    if (!IntersectsSegment(triangle.Bounds, from, delta, radius) ||
                        !triangle.FirstContact(from, delta, radius, out float fraction) || fraction >= first) continue;
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
                    first = fraction; found = true;
                }
            }
            return found;
        }
        private sealed class BodySurface
        {
            internal readonly CombatBodyDestruction.Piece Piece;
            private readonly HeadSurface skin;
            private readonly int[] indices;
            private readonly Vector3[] vertices, points;
            private Mesh gripBake;
            internal HeadTriangle[] Triangles;
            internal Bounds Bounds;
            internal bool Active;
            internal BodySurface(CombatBodyDestruction.Piece piece)
            {
                Piece = piece; skin = new HeadSurface(piece.Skin, true);
                indices = piece.Skin.sharedMesh.triangles; vertices = piece.Skin.sharedMesh.vertices;
                points = new Vector3[vertices.Length]; Triangles = new HeadTriangle[indices.Length / 3];
            }
            internal void Capture()
            {
                if (Piece.Debris) { Active = false; return; }
                if (Piece.Skin.enabled)
                {
                    Piece.Deformation?.Refresh(Piece.Skin);
                    if (Piece.Skin.sharedMesh.blendShapeCount == 0)
                    { skin.Capture(); Active = skin.Active; Bounds = skin.Bounds; Triangles = skin.Triangles; return; }
                    gripBake ??= new Mesh { name = "Posed body contact" };
                    Piece.Skin.BakeMesh(gripBake, true);
                    CaptureMesh(gripBake.vertices, Piece.Skin.transform.localToWorldMatrix); Active = true; return;
                }
                MeshRenderer released = Piece.Released;
                Active = released != null && released.enabled && released.gameObject.activeInHierarchy;
                if (!Active) return;
                // Baked vertices include the last live deformation and exact source scale.
                CaptureMesh(Piece.Baked.vertices, released.transform.localToWorldMatrix);
            }
            private void CaptureMesh(Vector3[] detached, Matrix4x4 matrix)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = matrix.MultiplyPoint3x4(detached[i]);
                    if (i == 0) Bounds = new Bounds(points[i], Vector3.zero); else Bounds.Encapsulate(points[i]);
                }
                if (Triangles == skin.Triangles) Triangles = new HeadTriangle[indices.Length / 3];
                for (int i = 0; i < Triangles.Length; i++)
                    Triangles[i] = new HeadTriangle(points[indices[i * 3]], points[indices[i * 3 + 1]], points[indices[i * 3 + 2]]);
            }
            internal void Dispose() { if (gripBake != null) Object.Destroy(gripBake); }
        }
    }
}
