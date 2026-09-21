using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Constrains the complete held prop by moving its arm, never its grip socket.
    /// The authored elbow/wrist angles survive shoulder correction unchanged.</summary>
    internal sealed class CombatWeaponConstraint : IDisposable
    {
        private const float Skin = .004f;
        private readonly CombatActor actor;
        private readonly Transform upper, forearm, hand, weapon;
        private readonly CapsuleCollider probe;
        private readonly CombatArmClearance armClearance;
        private readonly Collider[] nearby = new Collider[96];
        private readonly RaycastHit[] sweeps = new RaycastHit[48];
        private readonly List<Collider> obstacles = new List<Collider>(96);
        private readonly List<Collider> anatomy = new List<Collider>(20);
        private readonly List<Collider> otherAnatomy = new List<Collider>(20);
        private readonly Vector3[] searchAxes = new Vector3[5];
        private Quaternion baseUpper, baseForearm, baseHand;
        private Quaternion lastUpper, lastForearm, lastHand;
        private Pose lastWeapon;
        private bool applied, hasLast;
        private CombatActor other;
        private readonly Transform[] poseBones;
        private readonly Vector3[] safePositions;
        private readonly Quaternion[] safeRotations;
        private Vector3 safeRootPosition;
        private Quaternion safeRootRotation;
        private Pose[] previousAnatomy;
        private Pose[] previousOtherAnatomy = Array.Empty<Pose>();
        private readonly BodySweep[] anatomySweep;
        private BodySweep[] otherSweep = Array.Empty<BodySweep>();
        private readonly List<Bounds> worldSweepBounds = new List<Bounds>(96);
        private readonly Vector3[] fallbackBasePositions;
        private readonly Quaternion[] fallbackBaseRotations;
        private bool fullPoseApplied, contactPreview, pendingCommit, checkingDesiredPath;
        private Vector3 escapeAxis;
        private float escapeAngle;
        internal bool MotionBlocked { get; private set; }
        internal string BlockingShape { get; private set; }
        internal bool Blocked { get; private set; }
        internal bool WorldBlocked { get; private set; }
        internal float PenetrationDepth { get; private set; }

        internal CombatWeaponConstraint(CombatActor owner)
        {
            actor = owner; weapon = owner.Weapon.transform;
            foreach (Transform bone in owner.DamageRigRoot.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "upper_arm.R") upper = bone;
                else if (bone.name == "forearm.R") forearm = bone;
                else if (bone.name == "hand.R") hand = bone;
            }
            if (upper == null || forearm == null || hand == null)
                throw new InvalidOperationException("Held weapon clearance requires the complete right arm.");
            var ordered = new List<Transform>();
            foreach (SkinnedMeshRenderer skin in owner.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (Transform bone in skin.bones)
                    if (bone != null && !ordered.Contains(bone)) ordered.Add(bone);
            ordered.Sort((a, b) => DepthOf(a).CompareTo(DepthOf(b)));
            poseBones = ordered.ToArray();
            safePositions = new Vector3[poseBones.Length];
            safeRotations = new Quaternion[poseBones.Length];
            fallbackBasePositions = new Vector3[poseBones.Length];
            fallbackBaseRotations = new Quaternion[poseBones.Length];
            foreach (var entry in owner.Ragdoll.PhysicsController.AnatomicalColliders)
                anatomy.Add(entry.Key);
            previousAnatomy = new Pose[anatomy.Count];
            anatomySweep = new BodySweep[anatomy.Count];
            var query = new GameObject("Combat weapon clearance query") { hideFlags = HideFlags.HideAndDontSave };
            probe = query.AddComponent<CapsuleCollider>();
            probe.enabled = false; probe.direction = 1;
            armClearance = new CombatArmClearance(owner, upper, forearm);
        }

        internal void SetOpponent(CombatActor value)
        {
            other = value; otherAnatomy.Clear();
            if (other != null && other.Ragdoll != null)
                foreach (var entry in other.Ragdoll.PhysicsController.AnatomicalColliders) otherAnatomy.Add(entry.Key);
            previousOtherAnatomy = new Pose[otherAnatomy.Count];
            otherSweep = new BodySweep[otherAnatomy.Count];
            for (int i = 0; i < otherAnatomy.Count; i++)
                previousOtherAnatomy[i] = new Pose(otherAnatomy[i].transform.position, otherAnatomy[i].transform.rotation);
        }

        internal bool IsSupportArmPathClear(Vector3 shoulder, Vector3 elbow, Vector3 wrist) =>
            armClearance.IsSupportPathClear(shoulder, elbow, wrist);

        internal void Restore()
        {
            if (!applied) return;
            if (fullPoseApplied)
            {
                for (int i = 0; i < poseBones.Length; i++)
                    poseBones[i].SetPositionAndRotation(actor.transform.TransformPoint(fallbackBasePositions[i]),
                        actor.transform.rotation * fallbackBaseRotations[i]);
                fullPoseApplied = false;
            }
            upper.localRotation = baseUpper;
            forearm.localRotation = baseForearm;
            hand.localRotation = baseHand;
            applied = false;
        }

        internal void Forget() { applied = fullPoseApplied = hasLast = Blocked = WorldBlocked = MotionBlocked = pendingCommit = checkingDesiredPath = false; PenetrationDepth = 0f; }
        internal void Reset() { Restore(); Forget(); }

        internal void Apply()
        {
            if (weapon == null || actor.IsWeaponDropped ||
                (actor.IsRagdollActive && !actor.Ragdoll.IsRecovering)) { Forget(); return; }
            Restore();
            pendingCommit = false;
            MotionBlocked = false;
            baseUpper = upper.localRotation; baseForearm = forearm.localRotation; baseHand = hand.localRotation;
            Quaternion desired = upper.rotation;
            applied = true;
            GatherObstacles();
            escapeAngle = 0f;
            WorldBlocked = false;
            checkingDesiredPath = true;
            float depth = CandidateDepth();
            Vector3 firstEscapeAxis = escapeAxis;
            float firstEscapeAngle = escapeAngle;
            bool swept = hasLast && !SweepClear(lastWeapon, WeaponPose);
            checkingDesiredPath = false;
            Blocked = depth > 0f || swept;
            if (!Blocked) { AcceptCandidate(); return; }

            // A previous safe arm is a useful warm start while the torso keeps moving.
            // It does not pull a saved pre-fall arm back into a new recovery pose.
            Quaternion best = desired;
            float bestDepth = depth;
            if (hasLast)
            {
                upper.rotation = lastUpper;
                if (CandidateDepth() <= 0f && SweepClear(lastWeapon, WeaponPose))
                {
                    Quaternion safe = upper.rotation;
                    // Stop at first obstruction rather than stepping across a thin wall.
                    for (int i = 1; i <= 12; i++)
                    {
                        upper.rotation = Quaternion.Slerp(safe, desired, i / 12f);
                        if (CandidateDepth() > 0f || !SweepClear(lastWeapon, WeaponPose))
                        { upper.rotation = Quaternion.Slerp(safe, desired, (i - 1) / 12f); break; }
                    }
                    AcceptCandidate(); return;
                }
            }

            // Resolve shallow contacts by their actual separating normal. A few
            // millimetres of interference should not trigger a conspicuous arm swing.
            if (firstEscapeAngle > 0f)
                for (int scale = 1; scale <= 4; scale *= 2)
                {
                    upper.rotation = Quaternion.AngleAxis(Mathf.Min(36f, firstEscapeAngle * scale), firstEscapeAxis) * desired;
                    if (CandidateDepth() <= 0f && (!hasLast || SweepClear(lastWeapon, WeaponPose)))
                    { AcceptCandidate(); return; }
                }

            // Search around the shoulder, not the wrist. Both anatomical hinge angles
            // and the palm/cylinder transform remain exactly those of the authored pose.
            searchAxes[0] = actor.transform.up; searchAxes[1] = actor.transform.right; searchAxes[2] = actor.transform.forward;
            searchAxes[3] = (searchAxes[0] + searchAxes[1]).normalized;
            searchAxes[4] = (searchAxes[0] - searchAxes[1]).normalized;
            for (int ring = 1; ring <= 7; ring++)
            {
                float angle = ring * 12f;
                foreach (Vector3 axis in searchAxes)
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        upper.rotation = Quaternion.AngleAxis(angle * sign, axis) * desired;
                        float candidate = CandidateDepth();
                        if (candidate < bestDepth) { bestDepth = candidate; best = upper.rotation; }
                        if (candidate > 0f || (hasLast && !SweepClear(lastWeapon, WeaponPose))) continue;
                        AcceptCandidate(); return;
                    }
            }
            // A tightly obstructed action cannot be made safe by twisting the wrist.
            // Keep the last complete reachable arm while the owning action is blocked.
            if (hasLast)
            {
                upper.rotation = lastUpper;
                forearm.localRotation = lastForearm;
                hand.localRotation = lastHand;
                if (CandidateDepth() <= 0f && SweepClear(lastWeapon, WeaponPose)) { AcceptCandidate(); return; }
            }
            upper.rotation = best;
            forearm.localRotation = baseForearm; hand.localRotation = baseHand;
            PenetrationDepth = Depth();
            MotionBlocked = true;
            if (hasLast && !contactPreview)
            {
                for (int i = 0; i < poseBones.Length; i++)
                {
                    fallbackBasePositions[i] = actor.transform.InverseTransformPoint(poseBones[i].position);
                    fallbackBaseRotations[i] = Quaternion.Inverse(actor.transform.rotation) * poseBones[i].rotation;
                }
                fullPoseApplied = true;
                // Reject the complete obstructed step, including its body/root motion.
                // A free hand pose alone cannot repair a torso that carried it through a wall.
                if (actor.Body != null && actor.Body.enabled)
                    actor.Body.Move(safeRootPosition - actor.transform.position);
                else actor.transform.position = safeRootPosition;
                actor.transform.rotation = safeRootRotation;
                // Move can be clipped by a newly arrived obstacle. Keep the rig
                // attached to the root that actually moved, not the requested root.
                Vector3 remaining = actor.transform.position - safeRootPosition;
                for (int i = 0; i < poseBones.Length; i++)
                    poseBones[i].SetPositionAndRotation(safePositions[i] + remaining, safeRotations[i]);
                PenetrationDepth = Depth();
            }
            // Do not accept an intersecting pose as the sweep's next starting point.
        }

        private Pose WeaponPose => new Pose(weapon.position, weapon.rotation);

        private void AcceptCandidate()
        {
            PenetrationDepth = 0f;
            pendingCommit = !contactPreview;
        }

        /// <summary>Commit only after the supporting hand has made its final contact.</summary>
        internal void CommitPresentedPose(CombatSupportGrip support)
        {
            if (!pendingCommit || contactPreview) return;
            pendingCommit = false;
            bool clear = Depth() <= 0f && (!hasLast || SweepClear(lastWeapon, WeaponPose));
            if (!clear && support != null)
            {
                // A blocked supporting reach opens the hand; it cannot push its
                // forearm through the bar merely to satisfy a two-handed socket.
                support.RejectObstructedPose();
                clear = Depth() <= 0f && (!hasLast || SweepClear(lastWeapon, WeaponPose));
            }
            if (clear) Remember();
            else { MotionBlocked = Blocked = true; PenetrationDepth = Depth(); }
        }

        private void Remember()
        {
            if (contactPreview) return;
            lastUpper = upper.rotation; lastForearm = forearm.localRotation; lastHand = hand.localRotation;
            lastWeapon = WeaponPose; hasLast = true; PenetrationDepth = 0f;
            safeRootPosition = actor.transform.position; safeRootRotation = actor.transform.rotation;
            for (int i = 0; i < poseBones.Length; i++)
            { safePositions[i] = poseBones[i].position; safeRotations[i] = poseBones[i].rotation; }
            for (int i = 0; i < anatomy.Count; i++)
                previousAnatomy[i] = new Pose(anatomy[i].transform.position, anatomy[i].transform.rotation);
            for (int i = 0; i < otherAnatomy.Count; i++)
                if (otherAnatomy[i] != null)
                    previousOtherAnatomy[i] = new Pose(otherAnatomy[i].transform.position, otherAnatomy[i].transform.rotation);
        }

        private void GatherObstacles()
        {
            obstacles.Clear();
            int count = Physics.OverlapSphereNonAlloc(upper.position, 1.7f, nearby, ~0, QueryTriggerInteraction.Ignore);
            Collider[] found = count < nearby.Length ? nearby : Physics.OverlapSphere(upper.position, 1.7f, ~0, QueryTriggerInteraction.Ignore);
            if (found != nearby) count = found.Length;
            for (int i = 0; i < count; i++)
            {
                Collider hit = found[i];
                if (WorldObstacle(hit)) obstacles.Add(hit);
            }
            // Animated ragdoll colliders are disabled. Query their LIVE transforms
            // directly; never overwrite the frozen hurtbox snapshots used by damage.
            foreach (Collider shape in otherAnatomy)
                if (shape != null && !obstacles.Contains(shape)) obstacles.Add(shape);
        }

        private bool WorldObstacle(Collider hit) => hit != null && hit != probe && !hit.isTrigger &&
            !(hit is CharacterController) && !hit.transform.IsChildOf(actor.transform) &&
            !hit.transform.IsChildOf(weapon) && hit.GetComponentInParent<CombatActor>() == null;

        private float CandidateDepth()
        {
            if (!armClearance.IsClear())
            {
                // A body correction must not hide a simultaneous wall contact
                // from the attack preview's obstacle result.
                if (checkingDesiredPath)
                {
                    float weaponDepth = Depth();
                    if (weaponDepth > 0f) return weaponDepth;
                }
                BlockingShape = armClearance.LastBlockingShape;
                return .02f;
            }
            return Depth();
        }

        private float Depth()
        {
            float maximum = 0f;
            BlockingShape = null;
            Pose pose = WeaponPose;
            for (int index = 0; index < CombatWeaponGeometry.Segments.Count; index++)
            {
                CombatWeaponGeometry.Segment segment = CombatWeaponGeometry.Segments[index];
                Vector3 a = pose.position + pose.rotation * segment.A;
                Vector3 b = pose.position + pose.rotation * segment.B;
                Vector3 center = (a + b) * .5f;
                float radius = segment.Radius + Skin;
                if (Mathf.Min(a.y, b.y) - radius < actor.transform.position.y + .06f)
                {
                    float floorDepth = Mathf.Max(BelowFloor(a, radius), BelowFloor(b, radius));
                    if (floorDepth > 0f)
                    { if (checkingDesiredPath) WorldBlocked = true;
                        BlockingShape = "supporting floor"; SetEscape(a.y < b.y ? a : b, Vector3.up, floorDepth); return floorDepth; }
                }
                probe.radius = radius; probe.height = Vector3.Distance(a, b) + radius * 2f;
                Quaternion rotation = (b - a).sqrMagnitude > .000001f ? Quaternion.FromToRotation(Vector3.up, b - a) : Quaternion.identity;
                foreach (Collider obstacle in obstacles) { Check(obstacle); if (maximum > 0f && !checkingDesiredPath) return maximum; }
                foreach (Collider body in anatomy) { Check(body); if (maximum > 0f && !checkingDesiredPath) return maximum; }

                void Check(Collider shape)
                {
                    if (shape == null) return;
                    Vector3 scale = shape.transform.lossyScale;
                    float maximumScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                    Vector3 shapeCenter;
                    float bound;
                    if (shape is CapsuleCollider capsule)
                    { shapeCenter = shape.transform.TransformPoint(capsule.center); bound = Mathf.Max(capsule.height * .5f, capsule.radius) * maximumScale; }
                    else if (shape is BoxCollider box)
                    { shapeCenter = shape.transform.TransformPoint(box.center); bound = box.size.magnitude * .5f * maximumScale; }
                    else { shapeCenter = shape.bounds.center; bound = shape.bounds.extents.magnitude; }
                    float reach = bound + Vector3.Distance(a, b) * .5f + radius;
                    if ((center - shapeCenter).sqrMagnitude > reach * reach) return;
                    if (Physics.ComputePenetration(probe, center, rotation, shape, shape.transform.position,
                        shape.transform.rotation, out Vector3 direction, out float depth))
                    {
                        if (checkingDesiredPath && WorldObstacle(shape)) WorldBlocked = true;
                        if (depth > maximum)
                        { maximum = depth; BlockingShape = shape.name; SetEscape(center, direction, depth); }
                    }
                }
            }
            return maximum;
        }

        private void SetEscape(Vector3 point, Vector3 direction, float depth)
        {
            Vector3 lever = point - upper.position;
            escapeAxis = Vector3.Cross(lever, direction);
            float effectiveLever = Vector3.Cross(escapeAxis.normalized, lever).magnitude;
            escapeAngle = effectiveLever > .01f ? Mathf.Max(.5f, (depth + .002f) / effectiveLever * Mathf.Rad2Deg) : 0f;
            escapeAxis.Normalize();
        }

        private float BelowFloor(Vector3 point, float radius)
        {
            Vector3 origin = new Vector3(point.x, actor.transform.position.y + .5f, point.z);
            float length = origin.y - point.y + radius + .02f;
            if (length <= 0f) return 0f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, sweeps, length, ~0, QueryTriggerInteraction.Ignore);
            float depth = 0f;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = sweeps[i];
                if (!WorldObstacle(hit.collider) || hit.normal.y < .65f || hit.point.y > actor.transform.position.y + .06f) continue;
                depth = Mathf.Max(depth, hit.point.y - (point.y - radius));
            }
            return depth;
        }

        private bool SweepClear(Pose from, Pose to)
        {
            float angle = Quaternion.Angle(from.rotation, to.rotation);
            float weaponTravel = Vector3.Distance(from.position, to.position) + angle * Mathf.Deg2Rad * .7f;
            Bounds travelBounds = new Bounds((from.position + to.position) * .5f,
                Vector3.one * (1.4f + Vector3.Distance(from.position, to.position)));
            float bodyTravel = 0f;
            for (int i = 0; i < anatomy.Count; i++)
                if (anatomy[i] != null)
                {
                    anatomySweep[i] = new BodySweep(anatomy[i], previousAnatomy[i]);
                    bodyTravel = Mathf.Max(bodyTravel, anatomySweep[i].Travel);
                }
            bool nearOther = false;
            for (int i = 0; i < otherAnatomy.Count; i++)
            {
                Collider shape = otherAnatomy[i];
                if (shape == null) continue;
                otherSweep[i] = new BodySweep(shape, previousOtherAnatomy[i]);
                if (!otherSweep[i].TravelBounds.Intersects(travelBounds)) continue;
                nearOther = true;
                bodyTravel = Mathf.Max(bodyTravel, otherSweep[i].Travel);
            }
            // A planted hand does not make the torso or the other fighter static.
            // Bound relative surface travel, including rotation about each shape's
            // pivot, before choosing either the fast path or interpolation spacing.
            if (weaponTravel + bodyTravel < .001f) return true;
            int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max((weaponTravel + bodyTravel) / .025f,
                angle / 4f)), 1, 48);
            worldSweepBounds.Clear();
            foreach (Collider obstacle in obstacles)
                if (WorldObstacle(obstacle) && obstacle.bounds.Intersects(travelBounds)) worldSweepBounds.Add(obstacle.bounds);
            bool nearWorld = worldSweepBounds.Count > 0;
            Pose previous = from;
            bool anatomyBlocked = false;
            for (int step = 1; step <= steps; step++)
            {
                float t = step / (float)steps;
                Pose next = new Pose(Vector3.Lerp(from.position, to.position, t), Quaternion.Slerp(from.rotation, to.rotation, t));
                // Interpolate each body once per time sample, not once for every
                // weapon segment. The disabled rig shapes still use their live poses.
                for (int body = 0; body < anatomy.Count; body++)
                    if (anatomy[body] != null) anatomySweep[body].Sample(t);
                if (nearOther)
                    for (int body = 0; body < otherAnatomy.Count; body++)
                        if (otherAnatomy[body] != null) otherSweep[body].Sample(t);
                for (int index = 0; index < CombatWeaponGeometry.Segments.Count; index++)
                {
                    CombatWeaponGeometry.Segment segment = CombatWeaponGeometry.Segments[index];
                    Vector3 a = next.position + next.rotation * segment.A;
                    Vector3 b = next.position + next.rotation * segment.B;
                    Bounds segmentBounds = new Bounds(a, Vector3.zero);
                    segmentBounds.Encapsulate(b);
                    segmentBounds.Expand(segment.Radius * 2f);
                    probe.radius = segment.Radius;
                    probe.height = Vector3.Distance(a, b) + segment.Radius * 2f;
                    Quaternion capsuleRotation = (b - a).sqrMagnitude > .000001f ? Quaternion.FromToRotation(Vector3.up, b - a) : Quaternion.identity;
                    for (int body = 0; body < anatomy.Count; body++)
                    {
                        if (anatomy[body] == null || !anatomySweep[body].Bounds.Intersects(segmentBounds)) continue;
                        if (Physics.ComputePenetration(probe, (a + b) * .5f, capsuleRotation,
                            anatomy[body], anatomySweep[body].Pose.position, anatomySweep[body].Pose.rotation,
                            out _, out float overlap) && overlap > .001f)
                        {
                            if (!checkingDesiredPath || !nearWorld) return false;
                            anatomyBlocked = true;
                        }
                    }
                    if (nearOther)
                        for (int body = 0; body < otherAnatomy.Count; body++)
                        {
                            Collider shape = otherAnatomy[body];
                            if (shape == null || !otherSweep[body].Bounds.Intersects(segmentBounds)) continue;
                            if (Physics.ComputePenetration(probe, (a + b) * .5f, capsuleRotation, shape,
                                otherSweep[body].Pose.position, otherSweep[body].Pose.rotation,
                                out _, out float overlap) && overlap > .001f)
                            {
                                if (!checkingDesiredPath || !nearWorld) return false;
                                anatomyBlocked = true;
                            }
                        }
                    if (!nearWorld) continue;
                    Bounds segmentTravel = new Bounds(a, Vector3.zero);
                    segmentTravel.Encapsulate(b);
                    segmentTravel.Encapsulate(previous.position + previous.rotation * segment.A);
                    segmentTravel.Encapsulate(previous.position + previous.rotation * segment.B);
                    segmentTravel.Expand((segment.Radius + Skin) * 2f);
                    if (!NearWorld(segmentTravel)) continue;
                    int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(segment.A, segment.B) / (segment.Radius * 1.25f)));
                    for (int i = 0; i <= samples; i++)
                    {
                        Vector3 local = Vector3.Lerp(segment.A, segment.B, i / (float)samples);
                        Vector3 start = previous.position + previous.rotation * local;
                        Vector3 delta = next.position + next.rotation * local - start;
                        if (delta.sqrMagnitude <= .00000001f) continue;
                        Bounds pointTravel = new Bounds(start, Vector3.zero);
                        pointTravel.Encapsulate(start + delta);
                        pointTravel.Expand((segment.Radius + Skin) * 2f);
                        if (!NearWorld(pointTravel)) continue;
                        int count = Physics.SphereCastNonAlloc(start, segment.Radius + Skin, delta.normalized,
                            sweeps, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
                        if (count == sweeps.Length)
                        { if (checkingDesiredPath) WorldBlocked = true; return false; }
                        for (int h = 0; h < count; h++)
                            if (WorldObstacle(sweeps[h].collider))
                            { if (checkingDesiredPath) WorldBlocked = true; return false; }
                    }
                }
                previous = next;
            }
            return !anatomyBlocked;
        }

        private bool NearWorld(Bounds travel)
        {
            foreach (Bounds bounds in worldSweepBounds)
                if (bounds.Intersects(travel)) return true;
            return false;
        }

        private struct BodySweep
        {
            private readonly Pose from, to;
            private readonly CombatWeaponGeometry.ShapeBounds geometry;
            internal readonly float Travel;
            internal readonly Bounds TravelBounds;
            internal Pose Pose;
            internal Bounds Bounds;

            internal BodySweep(Collider shape, Pose previous)
            {
                from = previous;
                to = new Pose(shape.transform.position, shape.transform.rotation);
                geometry = new CombatWeaponGeometry.ShapeBounds(shape);
                float radius = geometry.PivotRadius;
                Travel = Vector3.Distance(from.position, to.position) + Quaternion.Angle(from.rotation, to.rotation) * Mathf.Deg2Rad * radius;
                TravelBounds = new Bounds(from.position, Vector3.zero);
                TravelBounds.Encapsulate(to.position);
                TravelBounds.Expand(radius * 2f);
                Pose = default; Bounds = default;
            }

            internal void Sample(float t)
            {
                Pose = new Pose(Vector3.Lerp(from.position, to.position, t), Quaternion.Slerp(from.rotation, to.rotation, t));
                Bounds = geometry.At(Pose.position, Pose.rotation);
            }
        }

        internal PreviewScope BeginContactPreview() => new PreviewScope(this);

        internal readonly struct PreviewScope : IDisposable
        {
            private readonly CombatWeaponConstraint owner;
            private readonly bool blocked, worldBlocked, motionBlocked;
            private readonly float penetration;
            private readonly string shape;
            internal PreviewScope(CombatWeaponConstraint constraint)
            {
                owner = constraint; blocked = owner.Blocked; motionBlocked = owner.MotionBlocked;
                worldBlocked = owner.WorldBlocked;
                penetration = owner.PenetrationDepth; shape = owner.BlockingShape; owner.contactPreview = true;
            }
            public void Dispose()
            {
                owner.contactPreview = false; owner.Blocked = blocked; owner.MotionBlocked = motionBlocked;
                owner.WorldBlocked = worldBlocked;
                owner.PenetrationDepth = penetration; owner.BlockingShape = shape;
            }
        }

        private static int DepthOf(Transform bone)
        { int depth = 0; while (bone != null) { depth++; bone = bone.parent; } return depth; }

        public void Dispose()
        {
            Forget();
            armClearance.Dispose();
            if (probe != null) UnityEngine.Object.Destroy(probe.gameObject);
        }
    }
}
