using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The complete generated crowbar in metres about its physical grip.
    /// Transform points by weapon.position/rotation: the wrapper cancels FBX unit scale.</summary>
    internal static class CombatWeaponGeometry
    {
        // A contact is admitted just outside the constraint's 4 mm clearance.
        // All outgoing queries use this same small skin, never a broad damage halo.
        internal const float ContactSkin = .005f;
        internal readonly struct Segment
        {
            public readonly Vector3 A, B;
            public readonly float Radius;
            public Segment(Vector3 a, Vector3 b, float radius) { A = a; B = b; Radius = radius; }
        }

        // Collider.bounds is empty for the disabled anatomical colliders. Keep a
        // conservative primitive box that can be placed at any sampled bone pose.
        internal readonly struct ShapeBounds
        {
            private readonly Vector3 center, extents;
            internal float PivotRadius => center.magnitude + extents.magnitude;

            internal ShapeBounds(Collider shape)
            {
                Vector3 signedScale = shape.transform.lossyScale;
                Vector3 scale = new Vector3(Mathf.Abs(signedScale.x), Mathf.Abs(signedScale.y), Mathf.Abs(signedScale.z));
                if (shape is CapsuleCollider capsule)
                {
                    center = Vector3.Scale(capsule.center, signedScale);
                    int axis = capsule.direction;
                    float radius = capsule.radius * Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
                    extents = Vector3.one * radius;
                    extents[axis] = Mathf.Max(capsule.height * scale[axis] * .5f, radius);
                }
                else if (shape is BoxCollider box)
                { center = Vector3.Scale(box.center, signedScale); extents = Vector3.Scale(box.size * .5f, scale); }
                else if (shape is SphereCollider sphere)
                {
                    center = Vector3.Scale(sphere.center, signedScale);
                    extents = Vector3.one * (sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)));
                }
                else
                {
                    center = Vector3.zero;
                    extents = Vector3.one * (Vector3.Distance(shape.transform.position, shape.bounds.center) + shape.bounds.extents.magnitude);
                }
            }

            internal Bounds At(Vector3 position, Quaternion rotation)
            {
                Vector3 x = rotation * new Vector3(extents.x, 0f, 0f);
                Vector3 y = rotation * new Vector3(0f, extents.y, 0f);
                Vector3 z = rotation * new Vector3(0f, 0f, extents.z);
                Vector3 worldExtents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                    Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
                return new Bounds(position + rotation * center, worldExtents * 2f);
            }
        }

        // CROWBAR_PATH and the wrap/chisel/claw in build-combat-test-3d-model.py.
        // Zero-length capsules bound the complete end boxes, including their corners.
        internal static IReadOnlyList<Segment> Segments { get; } = Array.AsReadOnly(new[]
        {
            new Segment(new Vector3(0f, -.11f, 0f), new Vector3(0f, .49f, 0f), .019f),
            new Segment(new Vector3(0f, .49f, 0f), new Vector3(0f, .55f, .022f), .019f),
            new Segment(new Vector3(0f, .55f, .022f), new Vector3(0f, .59f, .065f), .019f),
            new Segment(new Vector3(0f, .59f, .065f), new Vector3(0f, .605f, .112f), .019f),
            new Segment(new Vector3(0f, .605f, .112f), new Vector3(0f, .59f, .145f), .019f),
            new Segment(new Vector3(0f, -.075f, 0f), new Vector3(0f, .08f, 0f), .024f),
            new Segment(new Vector3(0f, -.119f, .012f), new Vector3(0f, -.119f, .012f), .03671f),
            new Segment(new Vector3(0f, .587f, .152f), new Vector3(0f, .587f, .152f), .02802f)
        });

        /// <summary>Continuous relative motion of both complete props, including a
        /// rotation's curved path. Disabled held colliders are not query data.</summary>
        internal static bool Sweep(Pose from, Pose to, Pose otherFrom, Pose otherTo,
            out float fraction, out Vector3 point, out Vector3 normal)
        {
            fraction = float.PositiveInfinity; point = normal = Vector3.zero;
            float angle = Quaternion.Angle(from.rotation, to.rotation) * Mathf.Deg2Rad;
            float otherAngle = Quaternion.Angle(otherFrom.rotation, otherTo.rotation) * Mathf.Deg2Rad;
            foreach (Segment a in Segments)
            {
                Bounds bounds = SweepBounds(a, from, to, angle);
                foreach (Segment b in Segments)
                {
                    if (!bounds.Intersects(SweepBounds(b, otherFrom, otherTo, otherAngle))) continue;
                    float speed = ((to.position - from.position) - (otherTo.position - otherFrom.position)).magnitude +
                        angle * Mathf.Max(a.A.magnitude, a.B.magnitude) + otherAngle * Mathf.Max(b.A.magnitude, b.B.magnitude);
                    // Distance between moving segments is Lipschitz-bounded by
                    // their maximum relative point travel. Conservative advancement
                    // cannot jump over two thin bars crossing between snapshots.
                    // Limit each interval's travel to 20 mm. Above the 0.1 mm
                    // contact tolerance every advance is at least 0.095 mm, so
                    // the 256-iteration budget can cover the entire interval;
                    // it cannot silently abandon a later crossing near parallel bars.
                    int intervals = Mathf.Max(1, Mathf.CeilToInt(speed / .02f));
                    for (int interval = 0; interval < intervals && interval / (float)intervals < fraction; interval++)
                    {
                        float t = interval / (float)intervals, end = (interval + 1f) / intervals;
                        for (int iteration = 0; iteration < 256 && t <= end && t < fraction; iteration++)
                        {
                            Pose first = At(from, to, t), second = At(otherFrom, otherTo, t);
                            Closest(first.position + first.rotation * a.A, first.position + first.rotation * a.B,
                                second.position + second.rotation * b.A, second.position + second.rotation * b.B,
                                out Vector3 p, out Vector3 q);
                            float distance = Vector3.Distance(p, q), gap = distance - a.Radius - b.Radius - ContactSkin * 2f;
                            if (gap <= .0001f)
                            {
                                fraction = t;
                                normal = distance > .000001f ? (p - q) / distance : (from.position - otherFrom.position).normalized;
                                if (normal.sqrMagnitude < .5f) normal = Vector3.up;
                                point = (p - normal * a.Radius + q + normal * b.Radius) * .5f;
                                break;
                            }
                            if (speed < .000001f || t == end) break;
                            float next = Mathf.Min(end, t + gap / speed * .95f);
                            if (next <= t) break;
                            t = next;
                        }
                    }
                }
            }
            return fraction <= 1f;
        }

        internal static Pose At(Pose from, Pose to, float t) =>
            new Pose(Vector3.Lerp(from.position, to.position, t), Quaternion.Slerp(from.rotation, to.rotation, t));

        private static Bounds SweepBounds(Segment segment, Pose from, Pose to, float angle)
        {
            Bounds bounds = new Bounds(from.position + from.rotation * segment.A, Vector3.zero);
            bounds.Encapsulate(from.position + from.rotation * segment.B);
            bounds.Encapsulate(to.position + to.rotation * segment.A);
            bounds.Encapsulate(to.position + to.rotation * segment.B);
            float bow = (1f - Mathf.Cos(angle * .5f)) * Mathf.Max(segment.A.magnitude, segment.B.magnitude);
            bounds.Expand((segment.Radius + ContactSkin + bow + .0001f) * 2f);
            return bounds;
        }

        private static void Closest(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 p, out Vector3 q)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r), s, t;
            if (a <= .00000001f && e <= .00000001f) { p = p1; q = p2; return; }
            if (a <= .00000001f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= .00000001f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denominator = a * e - b * b;
                    s = denominator > .00000001f ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            p = p1 + d1 * s; q = p2 + d2 * t;
        }
    }
}
