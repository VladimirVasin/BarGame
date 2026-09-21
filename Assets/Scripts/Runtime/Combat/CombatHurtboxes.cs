using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Queries a frozen copy of the posed anatomy without enabling ragdoll collision.
    /// Capture both fighters before sampling either weapon: their contact order cannot change anatomy.</summary>
    internal sealed class CombatHurtboxes
    {
        private readonly Shape[] shapes;
        private readonly Snapshot[] snapshots;

        internal readonly struct Hit
        {
            internal readonly Vector3 Point, Normal, Direction;
            internal readonly float Fraction;
            internal readonly Player3DAnatomicalPart Part;
            internal readonly Vector3 LocalPoint;
            internal readonly MeleeHitLocation Location;

            internal Hit(Vector3 point, Vector3 normal, Vector3 direction, float fraction,
                MeleeHitLocation location, Player3DAnatomicalPart part, Vector3 localPoint)
            { Point = point; Normal = normal; Direction = direction; Fraction = fraction; Location = location; Part = part; LocalPoint = localPoint; }
        }

        internal CombatHurtboxes(Transform rigRoot, Transform actorFrame, Player3DRagdollController ragdoll)
        {
            if (rigRoot == null || actorFrame == null || ragdoll == null || !ragdoll.IsInitialized)
                throw new ArgumentException("Combat hurtboxes require an initialized anatomical rig.");
            var bindPoses = new Dictionary<Transform, Matrix4x4>();
            foreach (SkinnedMeshRenderer skin in rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                Matrix4x4[] bind = skin.sharedMesh.bindposes;
                Transform[] bones = skin.bones;
                for (int i = 0; i < bones.Length && i < bind.Length; i++)
                    if (bones[i] != null && !bindPoses.ContainsKey(bones[i]))
                        bindPoses.Add(bones[i], skin.localToWorldMatrix * bind[i].inverse);
            }

            var ordered = new List<KeyValuePair<Collider, Player3DAnatomicalPart>>(ragdoll.AnatomicalColliders);
            ordered.Sort((a, b) =>
            {
                int region = Region(a.Value).CompareTo(Region(b.Value));
                return region != 0 ? region : a.Value.CompareTo(b.Value);
            });
            var measured = new List<Shape>(ordered.Count + 3);
            foreach (KeyValuePair<Collider, Player3DAnatomicalPart> entry in ordered)
            {
                Collider collider = entry.Key;
                Transform bone = collider.transform.parent;
                if (!bindPoses.TryGetValue(bone, out Matrix4x4 bind))
                    throw new InvalidOperationException("Combat anatomy lacks a skin bind pose for " + bone.name);
                measured.Add(new Shape(collider, bone, entry.Value, Region(entry.Value), bind, actorFrame));
            }
            Transform neck = FindBone("neck"), head = FindBone("head");
            Vector3 neckStart = bindPoses[neck].GetColumn(3), neckEnd = bindPoses[head].GetColumn(3);
            // Ragdoll joins head straight to chest and has no neck collision. Its exposed
            // neck remains a torso contact; this query-only capsule never enters PhysX.
            measured.Add(new Shape(neck, Player3DAnatomicalPart.Neck, MeleeBodyRegion.Torso, bindPoses[neck], actorFrame,
                neckStart, neckEnd, Vector3.Distance(neckStart, neckEnd) * .6f));
            AddHand("hand.L", "forearm.L", MeleeBodyRegion.LeftArm);
            AddHand("hand.R", "forearm.R", MeleeBodyRegion.RightArm);
            shapes = measured.ToArray();
            snapshots = new Snapshot[shapes.Length];
            Capture();

            void AddHand(string handName, string forearmName, MeleeBodyRegion region)
            {
                Transform hand = FindBone(handName), forearm = FindBone(forearmName);
                Matrix4x4 handBind = bindPoses[hand];
                Vector3 wrist = handBind.GetColumn(3), elbow = bindPoses[forearm].GetColumn(3);
                float radius = Vector3.Distance(wrist, elbow) * .22f;
                Vector3 center = wrist + (wrist - elbow).normalized * (radius * .6f);
                measured.Add(new Shape(hand, handName.EndsWith(".L", StringComparison.Ordinal) ? Player3DAnatomicalPart.LeftHand : Player3DAnatomicalPart.RightHand, region, handBind, actorFrame, center, radius));
            }

            Transform FindBone(string name)
            {
                foreach (Transform bone in bindPoses.Keys)
                    if (bone.name == name) return bone;
                throw new InvalidOperationException("Combat anatomy requires " + name);
            }
        }

        internal void Capture()
        {
            for (int i = 0; i < shapes.Length; i++) snapshots[i] = shapes[i].Capture();
        }

        internal bool GetRegionFrame(MeleeBodyRegion region, out Vector3 center, out Vector3 forward, out Vector3 up)
        {
            for (int i = 0; i < snapshots.Length; i++)
                if (snapshots[i].Region == region)
                { center = snapshots[i].Center; forward = snapshots[i].Forward; up = snapshots[i].Up; return true; }
            center = forward = up = Vector3.zero;
            return false;
        }

        internal bool SweepSphere(Vector3 from, Vector3 to, float radius, Vector3 direction, out Hit hit)
        {
            hit = default;
            if (!Finite(from) || !Finite(to) || !Finite(direction) || !float.IsFinite(radius) || radius < 0f)
                return false;
            Vector3 delta = to - from;
            direction = direction.sqrMagnitude > .000001f ? direction.normalized :
                delta.sqrMagnitude > .000001f ? delta.normalized : Vector3.forward;
            float first = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < snapshots.Length; i++)
            {
                Snapshot shape = snapshots[i];
                float reach = shape.BoundingRadius + radius;
                if ((ClosestOnSegment(from, to, shape.Center) - shape.Center).sqrMagnitude > reach * reach)
                    continue;
                if (!FirstContact(shape, from, delta, radius, out float fraction) || fraction >= first) continue;
                shape.Surface(from + delta * fraction, out Vector3 point, out Vector3 normal);
                Vector3 offset = point - shape.Center;
                MeleeHitLocation location = MeleeHitLocation.FromLocalSurface(shape.Region,
                    Vector3.Dot(offset, shape.Right), Vector3.Dot(offset, shape.Up), Vector3.Dot(offset, shape.Forward));
                first = fraction;
                hit = new Hit(point, normal, direction, fraction, location, shape.Part, shape.WorldToBone.MultiplyPoint3x4(point));
                found = true;
            }
            return found;
        }

        private static bool FirstContact(in Snapshot shape, Vector3 from, Vector3 delta, float radius, out float fraction)
        {
            float threshold = radius * radius;
            fraction = 0f;
            if (shape.DistanceSquared(from) <= threshold) return true;
            if (delta.sqrMagnitude < .0000000001f) return false;
            // Distance to a convex capsule/box along a line is convex. Find its minimum,
            // then bisect the first entry; no sampling gaps can skip a thin limb.
            float low = 0f, high = 1f;
            for (int step = 0; step < 24; step++)
            {
                float left = (2f * low + high) / 3f, right = (low + 2f * high) / 3f;
                if (shape.DistanceSquared(from + delta * left) <= shape.DistanceSquared(from + delta * right)) high = right;
                else low = left;
            }
            float minimum = (low + high) * .5f;
            if (shape.DistanceSquared(from + delta) < shape.DistanceSquared(from + delta * minimum)) minimum = 1f;
            if (shape.DistanceSquared(from + delta * minimum) > threshold + .00000001f) return false;
            low = 0f; high = minimum;
            for (int step = 0; step < 24; step++)
            {
                float middle = (low + high) * .5f;
                if (shape.DistanceSquared(from + delta * middle) <= threshold) high = middle;
                else low = middle;
            }
            fraction = high;
            return true;
        }

        private sealed class Shape
        {
            private readonly Transform geometry, bone;
            private readonly MeleeBodyRegion region;
            private readonly Player3DAnatomicalPart part;
            private readonly Vector3 localForward, localUp, center, halfSize, customHalfAxis;
            private readonly float radius, halfHeight;
            private readonly int capsuleAxis;
            private readonly bool box, customCapsule;

            internal Shape(Collider collider, Transform bone, Player3DAnatomicalPart part, MeleeBodyRegion region, Matrix4x4 bind, Transform actor)
            {
                geometry = collider.transform; this.bone = bone; this.part = part; this.region = region;
                localForward = bind.inverse.MultiplyVector(actor.forward).normalized;
                localUp = bind.inverse.MultiplyVector(actor.up).normalized;
                if (collider is BoxCollider cube)
                { box = true; center = cube.center; halfSize = cube.size * .5f; }
                else if (collider is CapsuleCollider capsule)
                { center = capsule.center; radius = capsule.radius; halfHeight = capsule.height * .5f; capsuleAxis = capsule.direction; }
                else throw new InvalidOperationException("Unsupported combat anatomical collider: " + collider.GetType().Name);
            }

            internal Shape(Transform hand, Player3DAnatomicalPart part, MeleeBodyRegion region, Matrix4x4 bind, Transform actor,
                Vector3 worldCenter, float worldRadius)
            {
                geometry = bone = hand; this.part = part; this.region = region;
                localForward = bind.inverse.MultiplyVector(actor.forward).normalized;
                localUp = bind.inverse.MultiplyVector(actor.up).normalized;
                center = bind.inverse.MultiplyPoint3x4(worldCenter);
                float scale = Mathf.Max(bind.MultiplyVector(Vector3.right).magnitude,
                    Mathf.Max(bind.MultiplyVector(Vector3.up).magnitude, bind.MultiplyVector(Vector3.forward).magnitude));
                radius = worldRadius / scale;
                // Zero half-height makes a capsule into the measured palm sphere.
                halfHeight = 0f; capsuleAxis = 1;
            }

            internal Shape(Transform bone, Player3DAnatomicalPart part, MeleeBodyRegion region, Matrix4x4 bind, Transform actor,
                Vector3 start, Vector3 end, float worldRadius)
                : this(bone, part, region, bind, actor, (start + end) * .5f, worldRadius)
            {
                customCapsule = true;
                customHalfAxis = bind.inverse.MultiplyVector((end - start) * .5f);
            }

            internal Snapshot Capture()
            {
                Vector3 x = geometry.TransformVector(Vector3.right), y = geometry.TransformVector(Vector3.up),
                    z = geometry.TransformVector(Vector3.forward);
                Vector3 scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
                Vector3 forward = bone.TransformVector(localForward).normalized;
                Vector3 up = Vector3.ProjectOnPlane(bone.TransformVector(localUp), forward).normalized;
                Vector3 axis = capsuleAxis == 0 ? x.normalized : capsuleAxis == 1 ? y.normalized : z.normalized;
                float axisScale = scale[capsuleAxis];
                float radialScale = capsuleAxis == 0 ? Mathf.Max(scale.y, scale.z) :
                    capsuleAxis == 1 ? Mathf.Max(scale.x, scale.z) : Mathf.Max(scale.x, scale.y);
                float worldRadius = radius * radialScale;
                float halfSegment = Mathf.Max(0f, halfHeight * axisScale - worldRadius);
                if (customCapsule)
                {
                    Vector3 halfAxis = geometry.TransformVector(customHalfAxis);
                    axis = halfAxis.sqrMagnitude > .00000001f ? halfAxis.normalized : up;
                    worldRadius = radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                    halfSegment = Mathf.Max(0f, halfAxis.magnitude - worldRadius);
                }
                return new Snapshot(part, bone.worldToLocalMatrix, region, geometry.TransformPoint(center), forward, up, box,
                    x.normalized, y.normalized, z.normalized, Vector3.Scale(halfSize, scale),
                    axis, worldRadius, halfSegment);
            }
        }

        private readonly struct Snapshot
        {
            internal readonly MeleeBodyRegion Region;
            internal readonly Player3DAnatomicalPart Part;
            internal readonly Matrix4x4 WorldToBone;
            internal readonly Vector3 Center, Forward, Up, Right;
            internal readonly float BoundingRadius;
            private readonly bool box;
            private readonly Vector3 x, y, z, halfSize, axis;
            private readonly float radius, halfSegment;

            internal Snapshot(Player3DAnatomicalPart part, Matrix4x4 worldToBone, MeleeBodyRegion region, Vector3 center, Vector3 forward, Vector3 up, bool box,
                Vector3 x, Vector3 y, Vector3 z, Vector3 halfSize, Vector3 axis, float radius, float halfSegment)
            {
                Part = part; WorldToBone = worldToBone; Region = region; Center = center; Forward = forward; Up = up; Right = Vector3.Cross(up, forward).normalized;
                this.box = box; this.x = x; this.y = y; this.z = z; this.halfSize = halfSize;
                this.axis = axis; this.radius = radius; this.halfSegment = halfSegment;
                BoundingRadius = box ? halfSize.magnitude : radius + halfSegment;
            }

            internal float DistanceSquared(Vector3 point)
            {
                Vector3 offset = point - Center;
                if (box)
                {
                    float dx = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(offset, x)) - halfSize.x);
                    float dy = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(offset, y)) - halfSize.y);
                    float dz = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(offset, z)) - halfSize.z);
                    return dx * dx + dy * dy + dz * dz;
                }
                float along = Mathf.Clamp(Vector3.Dot(offset, axis), -halfSegment, halfSegment);
                float distance = Mathf.Max(0f, (offset - axis * along).magnitude - radius);
                return distance * distance;
            }

            internal void Surface(Vector3 point, out Vector3 surface, out Vector3 normal)
            {
                Vector3 offset = point - Center;
                if (!box)
                {
                    float along = Mathf.Clamp(Vector3.Dot(offset, axis), -halfSegment, halfSegment);
                    Vector3 axisPoint = Center + axis * along;
                    normal = point - axisPoint;
                    if (normal.sqrMagnitude < .00000001f)
                    {
                        // A sweep starting on the centreline contains no evidence of a rear hit.
                        normal = Vector3.ProjectOnPlane(Forward, axis);
                        if (normal.sqrMagnitude < .00000001f) normal = Vector3.ProjectOnPlane(Right, axis);
                    }
                    normal.Normalize();
                    surface = axisPoint + normal * radius;
                    return;
                }
                Vector3 local = new Vector3(Vector3.Dot(offset, x), Vector3.Dot(offset, y), Vector3.Dot(offset, z));
                Vector3 closest = new Vector3(Mathf.Clamp(local.x, -halfSize.x, halfSize.x),
                    Mathf.Clamp(local.y, -halfSize.y, halfSize.y), Mathf.Clamp(local.z, -halfSize.z, halfSize.z));
                Vector3 outside = local - closest;
                if (outside.sqrMagnitude > .00000001f)
                    normal = (x * outside.x + y * outside.y + z * outside.z).normalized;
                else
                {
                    int face = 0;
                    float gap = halfSize.x - Mathf.Abs(local.x);
                    for (int i = 1; i < 3; i++)
                        if (halfSize[i] - Mathf.Abs(local[i]) < gap)
                        { face = i; gap = halfSize[i] - Mathf.Abs(local[i]); }
                    Vector3 faceAxis = face == 0 ? x : face == 1 ? y : z;
                    float sign = Mathf.Abs(local[face]) > .000001f ? Mathf.Sign(local[face]) :
                        Vector3.Dot(faceAxis, Forward) >= 0f ? 1f : -1f;
                    closest[face] = halfSize[face] * sign;
                    normal = faceAxis * sign;
                }
                surface = Center + x * closest.x + y * closest.y + z * closest.z;
            }
        }

        private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 point)
        {
            Vector3 delta = b - a;
            return a + delta * (delta.sqrMagnitude > .0000000001f ? Mathf.Clamp01(Vector3.Dot(point - a, delta) / delta.sqrMagnitude) : 0f);
        }

        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static MeleeBodyRegion Region(Player3DAnatomicalPart part) => part switch
        {
            Player3DAnatomicalPart.Head => MeleeBodyRegion.Head,
            Player3DAnatomicalPart.LeftUpperArm or Player3DAnatomicalPart.LeftForearm or Player3DAnatomicalPart.LeftHand => MeleeBodyRegion.LeftArm,
            Player3DAnatomicalPart.RightUpperArm or Player3DAnatomicalPart.RightForearm or Player3DAnatomicalPart.RightHand => MeleeBodyRegion.RightArm,
            Player3DAnatomicalPart.LeftThigh or Player3DAnatomicalPart.LeftShin or Player3DAnatomicalPart.LeftFoot => MeleeBodyRegion.LeftLeg,
            Player3DAnatomicalPart.RightThigh or Player3DAnatomicalPart.RightShin or Player3DAnatomicalPart.RightFoot => MeleeBodyRegion.RightLeg,
            _ => MeleeBodyRegion.Torso
        };
    }
}
