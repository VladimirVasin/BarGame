using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The complete generated crowbar in metres about its physical grip.
    /// Transform points by weapon.position/rotation: the wrapper cancels FBX unit scale.</summary>
    internal static class CombatWeaponGeometry
    {
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
    }
}
