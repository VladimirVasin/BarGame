using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal sealed partial class CombatHurtboxes
    {
        private readonly Dictionary<SkinnedMeshRenderer, HeadSurface> headSurfaceCache = new Dictionary<SkinnedMeshRenderer, HeadSurface>();
        private HeadSurface[] intactHeadSurfaces, activeHeadSurfaces;
        private Matrix4x4 frozenHeadToLocal;
        private Vector3 frozenHeadCentre, frozenHeadForward, frozenHeadUp, frozenHeadRight;
        private Vector3 headLocalCentre, headLocalForward, headLocalUp;

        // These surfaces are anatomy. Hair/headwear and wound overlays must not
        // widen a bullet target, even when they travel with a fractured sector.
        internal static bool IsHeadFlesh(string name) => name == "GEO_Head" || name == "GEO_FaceSurface" ||
            name.StartsWith("GEO_Ear.", StringComparison.Ordinal) || name.StartsWith("GEO_EarFold.", StringComparison.Ordinal);

        private void InitializeHeadSurfaces(Transform rigRoot)
        {
            var surfaces = new List<HeadSurface>();
            foreach (SkinnedMeshRenderer skin in rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (IsHeadFlesh(skin.name)) surfaces.Add(RequireHeadSurface(skin));
            if (surfaces.Count == 0) throw new InvalidOperationException("Combat bullets require authored head surfaces.");
            intactHeadSurfaces = activeHeadSurfaces = surfaces.ToArray();
            headLocalForward = headBind.inverse.MultiplyVector(actorFrame.forward).normalized;
            headLocalUp = headBind.inverse.MultiplyVector(actorFrame.up).normalized;
            // Keep the anatomical classification frame independent of which
            // surviving sector happens to be first in the query list.
            Bounds bounds = default;
            bool measured = false;
            foreach (HeadSurface surface in intactHeadSurfaces)
            {
                surface.Capture();
                if (!surface.Active) continue;
                foreach (Vector3 point in surface.Points)
                {
                    Vector3 local = headBone.InverseTransformPoint(point);
                    if (!measured) { bounds = new Bounds(local, Vector3.zero); measured = true; }
                    else bounds.Encapsulate(local);
                }
            }
            if (!measured) throw new InvalidOperationException("Combat bullets require a visible anatomical head.");
            headLocalCentre = bounds.center;
        }

        private HeadSurface RequireHeadSurface(SkinnedMeshRenderer skin)
        {
            if (!headSurfaceCache.TryGetValue(skin, out HeadSurface surface))
                headSurfaceCache.Add(skin, surface = new HeadSurface(skin));
            return surface;
        }

        private void SetHeadSurfaces(IReadOnlyList<SkinnedMeshRenderer> retained)
        {
            if (retained == null) { activeHeadSurfaces = intactHeadSurfaces; return; }
            var replacement = new HeadSurface[retained.Count];
            for (int i = 0; i < replacement.Length; i++) replacement[i] = RequireHeadSurface(retained[i]);
            activeHeadSurfaces = replacement;
        }

        private void CaptureHeadSurfaces()
        {
            frozenHeadToLocal = headBone.worldToLocalMatrix;
            frozenHeadCentre = headBone.TransformPoint(headLocalCentre);
            frozenHeadForward = headBone.TransformVector(headLocalForward).normalized;
            frozenHeadUp = Vector3.ProjectOnPlane(headBone.TransformVector(headLocalUp), frozenHeadForward).normalized;
            frozenHeadRight = Vector3.Cross(frozenHeadUp, frozenHeadForward).normalized;
            foreach (HeadSurface surface in activeHeadSurfaces) surface.Capture();
        }

        internal bool SweepProjectile(Vector3 from, Vector3 to, float radius, Vector3 direction, out Hit hit)
        {
            hit = default;
            if (!Finite(from) || !Finite(to) || !Finite(direction) || !float.IsFinite(radius) || radius < 0f) return false;
            Vector3 delta = to - from;
            direction = direction.sqrMagnitude > .000001f ? direction.normalized :
                delta.sqrMagnitude > .000001f ? delta.normalized : Vector3.forward;
            bool found = SweepSphere(from, to, radius, direction, false, out hit);
            float first = found ? hit.Fraction : float.PositiveInfinity;
            foreach (HeadSurface surface in activeHeadSurfaces)
            {
                if (!surface.Active || !IntersectsSegment(surface.Bounds, from, delta, radius)) continue;
                foreach (HeadTriangle triangle in surface.Triangles)
                {
                    if (!IntersectsSegment(triangle.Bounds, from, delta, radius) ||
                        !triangle.FirstContact(from, delta, radius, out float fraction) || fraction >= first) continue;
                    Vector3 sphereCentre = from + delta * fraction;
                    Vector3 point = triangle.Closest(sphereCentre);
                    Vector3 normal = sphereCentre - point;
                    if (normal.sqrMagnitude < .00000001f)
                    {
                        normal = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).normalized;
                        if (Vector3.Dot(normal, direction) > 0f) normal = -normal;
                    }
                    else normal.Normalize();
                    Vector3 offset = point - frozenHeadCentre;
                    MeleeHitLocation location = MeleeHitLocation.FromLocalSurface(MeleeBodyRegion.Head,
                        Vector3.Dot(offset, frozenHeadRight), Vector3.Dot(offset, frozenHeadUp), Vector3.Dot(offset, frozenHeadForward));
                    hit = new Hit(point, normal, direction, fraction, location, Player3DAnatomicalPart.Head,
                        frozenHeadToLocal.MultiplyPoint3x4(point), frozenHeadToLocal.MultiplyVector(direction).normalized);
                    first = fraction; found = true;
                }
            }
            return found;
        }

        private static bool IntersectsSegment(Bounds bounds, Vector3 from, Vector3 delta, float radius)
        {
            Vector3 min = bounds.min - Vector3.one * radius, max = bounds.max + Vector3.one * radius;
            float near = 0f, far = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(delta[axis]) < .00000001f)
                { if (from[axis] < min[axis] || from[axis] > max[axis]) return false; continue; }
                float a = (min[axis] - from[axis]) / delta[axis], b = (max[axis] - from[axis]) / delta[axis];
                near = Mathf.Max(near, Mathf.Min(a, b)); far = Mathf.Min(far, Mathf.Max(a, b));
                if (near > far) return false;
            }
            return true;
        }

        private sealed class HeadSurface
        {
            private readonly SkinnedMeshRenderer source;
            private readonly Vector3[] vertices;
            private readonly BoneWeight[] weights;
            private readonly Matrix4x4[] bind, posedBones;
            private readonly Transform[] bones;
            private readonly int[] indices;
            internal readonly Vector3[] Points;
            internal readonly HeadTriangle[] Triangles;
            internal Bounds Bounds;
            internal bool Active;

            internal HeadSurface(SkinnedMeshRenderer source)
            {
                this.source = source;
                Mesh mesh = source.sharedMesh;
                vertices = mesh.vertices; weights = mesh.boneWeights; bind = mesh.bindposes;
                bones = source.bones; posedBones = new Matrix4x4[bones.Length]; indices = mesh.triangles;
                Points = new Vector3[vertices.Length]; Triangles = new HeadTriangle[indices.Length / 3];
                if (weights.Length != vertices.Length || bind.Length != bones.Length)
                    throw new InvalidOperationException("Combat head surface requires complete skin weights: " + source.name);
            }

            internal void Capture()
            {
                Active = source != null && source.gameObject.activeInHierarchy &&
                    (source.enabled || Player3DHeadVisibility.IsTemporarilyHidden(source));
                if (!Active) return;
                for (int i = 0; i < bones.Length; i++) posedBones[i] = bones[i].localToWorldMatrix * bind[i];
                for (int i = 0; i < vertices.Length; i++)
                {
                    BoneWeight weight = weights[i]; Vector3 vertex = vertices[i];
                    Vector3 point = posedBones[weight.boneIndex0].MultiplyPoint3x4(vertex) * weight.weight0;
                    if (weight.weight1 > 0f) point += posedBones[weight.boneIndex1].MultiplyPoint3x4(vertex) * weight.weight1;
                    if (weight.weight2 > 0f) point += posedBones[weight.boneIndex2].MultiplyPoint3x4(vertex) * weight.weight2;
                    if (weight.weight3 > 0f) point += posedBones[weight.boneIndex3].MultiplyPoint3x4(vertex) * weight.weight3;
                    Points[i] = point;
                    if (i == 0) Bounds = new Bounds(point, Vector3.zero); else Bounds.Encapsulate(point);
                }
                for (int i = 0; i < Triangles.Length; i++)
                    Triangles[i] = new HeadTriangle(Points[indices[i * 3]], Points[indices[i * 3 + 1]], Points[indices[i * 3 + 2]]);
            }
        }

        private readonly struct HeadTriangle
        {
            internal readonly Vector3 A, B, C;
            internal readonly Bounds Bounds;
            internal HeadTriangle(Vector3 a, Vector3 b, Vector3 c)
            {
                A = a; B = b; C = c;
                Bounds = new Bounds(a, Vector3.zero); Bounds.Encapsulate(b); Bounds.Encapsulate(c);
            }

            // Closest point on the actual triangle, including its edges/vertices.
            // Degenerate authored triangles reduce to their finite line segments.
            internal Vector3 Closest(Vector3 point)
            {
                Vector3 ab = B - A, ac = C - A;
                if (Vector3.Cross(ab, ac).sqrMagnitude < .0000000000000001f)
                {
                    Vector3 p = ClosestOnSegment(A, B, point), q = ClosestOnSegment(B, C, point), r = ClosestOnSegment(C, A, point);
                    if ((q - point).sqrMagnitude < (p - point).sqrMagnitude) p = q;
                    return (r - point).sqrMagnitude < (p - point).sqrMagnitude ? r : p;
                }
                Vector3 ap = point - A;
                float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
                if (d1 <= 0f && d2 <= 0f) return A;
                Vector3 bp = point - B;
                float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
                if (d3 >= 0f && d4 <= d3) return B;
                float vc = d1 * d4 - d3 * d2;
                if (vc <= 0f && d1 >= 0f && d3 <= 0f) return A + ab * (d1 / (d1 - d3));
                Vector3 cp = point - C;
                float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
                if (d6 >= 0f && d5 <= d6) return C;
                float vb = d5 * d2 - d1 * d6;
                if (vb <= 0f && d2 >= 0f && d6 <= 0f) return A + ac * (d2 / (d2 - d6));
                float va = d3 * d6 - d5 * d4;
                if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                    return B + (C - B) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
                float inverse = 1f / (va + vb + vc);
                return A + ab * (vb * inverse) + ac * (vc * inverse);
            }

            internal bool FirstContact(Vector3 from, Vector3 delta, float radius, out float fraction)
            {
                float threshold = radius * radius + .00000001f;
                fraction = 0f;
                if (DistanceSquared(from) <= threshold) return true;
                if (delta.sqrMagnitude < .0000000001f) return false;
                // Distance to one convex triangle is convex along a segment.
                // Minimize before bisecting entry so even a grazing ear cannot
                // fall between discrete bullet samples.
                float low = 0f, high = 1f;
                for (int pass = 0; pass < 32; pass++)
                {
                    float left = (2f * low + high) / 3f, right = (low + 2f * high) / 3f;
                    if (DistanceSquared(from + delta * left) <= DistanceSquared(from + delta * right)) high = right;
                    else low = left;
                }
                float minimum = (low + high) * .5f;
                if (DistanceSquared(from + delta) < DistanceSquared(from + delta * minimum)) minimum = 1f;
                if (DistanceSquared(from + delta * minimum) > threshold) return false;
                low = 0f; high = minimum;
                for (int pass = 0; pass < 24; pass++)
                {
                    float middle = (low + high) * .5f;
                    if (DistanceSquared(from + delta * middle) <= threshold) high = middle; else low = middle;
                }
                fraction = high;
                return true;
            }

            private float DistanceSquared(Vector3 point) => (point - Closest(point)).sqrMagnitude;
        }
    }
}
