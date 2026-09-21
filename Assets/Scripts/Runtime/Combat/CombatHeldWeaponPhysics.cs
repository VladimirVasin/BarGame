using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Collision ownership follows the arm during a fall and the weapon after release.
    /// No collider in this component moves the rendered weapon independently of its hand.</summary>
    [DefaultExecutionOrder(500)]
    internal sealed class CombatHeldWeaponPhysics : MonoBehaviour
    {
        private const float ReleaseContactSeconds = .08f;
        private readonly List<ReleasePair> releasePairs = new List<ReleasePair>();
        private readonly List<Collider> rightContact = new List<Collider>(), leftContact = new List<Collider>();
        private Transform weapon;
        private Rigidbody weaponBody, forearm;
        private Joint[] armJoints = Array.Empty<Joint>();
        private bool[] armJointCollisions = Array.Empty<bool>();
        private bool forearmPropertiesCaptured;
        private float forearmMass;
        private Vector3 forearmCentre, forearmInertia;
        private Quaternion forearmInertiaRotation;
        private NpcHandPose hands;
        private CapsuleCollider[] held = Array.Empty<CapsuleCollider>(), dropped = Array.Empty<CapsuleCollider>();
        internal bool IsHeld { get; private set; }
        internal bool IsDropped { get; private set; }
        internal int DroppedAnatomyContactCount { get; private set; }
        internal Player3DAnatomicalPart LastDroppedAnatomyPart { get; private set; }
        private const float DroppedSkin = .001f;
        private readonly List<Collider> opponentAnatomy = new List<Collider>();
        private readonly List<Player3DAnatomicalPart> opponentParts = new List<Player3DAnatomicalPart>();
        private Pose[] previousAnatomy = Array.Empty<Pose>(), currentAnatomy = Array.Empty<Pose>();
        private readonly RaycastHit[] droppedCasts = new RaycastHit[64];
        private readonly Collider[] droppedOverlaps = new Collider[64];
        private Pose previousDropped;
        private bool hasDroppedSnapshot;

        private struct ReleasePair
        {
            internal Collider Weapon, Hand;
            internal float Remaining;
        }

        internal void SetOpponent(CombatActor opponent)
        {
            opponentAnatomy.Clear(); opponentParts.Clear();
            if (opponent != null && opponent.Ragdoll != null)
                foreach (var entry in opponent.Ragdoll.PhysicsController.AnatomicalColliders)
                { opponentAnatomy.Add(entry.Key); opponentParts.Add(entry.Value); }
            previousAnatomy = new Pose[opponentAnatomy.Count];
            currentAnatomy = new Pose[opponentAnatomy.Count];
            hasDroppedSnapshot = false;
        }

        internal void Initialize(Transform targetWeapon, Rigidbody targetBody, Rigidbody rightForearm,
            IReadOnlyDictionary<Collider, Player3DAnatomicalPart> anatomy, NpcHandPose handPose)
        {
            if (weapon != null) throw new InvalidOperationException("Held weapon physics is already initialized.");
            weapon = targetWeapon != null ? targetWeapon : throw new ArgumentNullException(nameof(targetWeapon));
            weaponBody = targetBody != null ? targetBody : throw new ArgumentNullException(nameof(targetBody));
            forearm = rightForearm != null ? rightForearm : throw new ArgumentNullException(nameof(rightForearm));
            hands = handPose;
            foreach (var entry in anatomy)
            {
                if (entry.Value == Player3DAnatomicalPart.RightForearm || entry.Value == Player3DAnatomicalPart.RightHand)
                    rightContact.Add(entry.Key);
                if (entry.Value == Player3DAnatomicalPart.LeftForearm || entry.Value == Player3DAnatomicalPart.LeftHand)
                    leftContact.Add(entry.Key);
            }
            held = CreateShapes(forearm.transform, "Held Crowbar Collision");
            dropped = CreateShapes(weapon, "Dropped Crowbar Collision");
        }

        internal void EnableHeld()
        {
            if (IsHeld || weapon == null || forearm == null) return;
            // Initialize runs while anatomical collision is disabled. Capture once
            // at the first physical handoff, before the held shapes change this body.
            if (!forearmPropertiesCaptured)
            {
                forearmMass = forearm.mass;
                forearmCentre = forearm.centerOfMass;
                forearmInertia = forearm.inertiaTensor;
                forearmInertiaRotation = forearm.inertiaTensorRotation;
                forearmPropertiesCaptured = true;
            }
            Vector3 previousCentre = forearm.worldCenterOfMass;
            Vector3 previousVelocity = forearm.linearVelocity;
            ClearReleasePairs();
            SetEnabled(dropped, false);
            weaponBody.isKinematic = true;
            weaponBody.useGravity = false;
            weaponBody.detectCollisions = false;
            // The ragdoll already ignores adjacent anatomical collider pairs.
            // A joint-wide exclusion would also let the new shaft cross the
            // upper arm, so permit the compound's additional colliders here.
            // Combat activation replaces the elbow's reference frame before
            // enabling this compound; do not retain its retired joint.
            armJoints = forearm.GetComponents<Joint>();
            armJointCollisions = new bool[armJoints.Length];
            for (int i = 0; i < armJoints.Length; i++)
                if (armJoints[i] != null)
                { armJointCollisions[i] = armJoints[i].enableCollision; armJoints[i].enableCollision = true; }
            PlaceShapes(held);
            SetEnabled(held, true);
            forearm.mass = forearmMass + weaponBody.mass;
            Physics.SyncTransforms();
            forearm.ResetCenterOfMass();
            forearm.ResetInertiaTensor();
            PreserveArmMotion(previousCentre, previousVelocity);
            IsHeld = true; IsDropped = false;
            // The right forearm and these colliders are one body. Only an existing
            // left palm contact gets a brief release allowance; torso/legs stay solid.
            if (hands != null && hands.LeftGripWeight > .5f) AllowInitialContact(held, leftContact);
        }

        internal void DisableHeld()
        {
            Vector3 previousCentre = forearm != null ? forearm.worldCenterOfMass : Vector3.zero;
            Vector3 previousVelocity = forearm != null ? forearm.linearVelocity : Vector3.zero;
            SetEnabled(held, false);
            if (IsHeld)
            {
                for (int i = 0; i < armJoints.Length; i++)
                    if (armJoints[i] != null) armJoints[i].enableCollision = armJointCollisions[i];
                if (forearm != null && forearmPropertiesCaptured)
                {
                    // Recovery may already have disabled every anatomical collider.
                    // Recalculating now would describe an empty body, not the arm.
                    forearm.mass = forearmMass;
                    forearm.centerOfMass = forearmCentre;
                    forearm.inertiaTensor = forearmInertia;
                    forearm.inertiaTensorRotation = forearmInertiaRotation;
                    PreserveArmMotion(previousCentre, previousVelocity);
                }
            }
            IsHeld = false;
            ClearReleasePairs();
        }

        private void PreserveArmMotion(Vector3 previousCentre, Vector3 previousVelocity)
        {
            if (forearm != null && !forearm.isKinematic)
                forearm.linearVelocity = previousVelocity + Vector3.Cross(forearm.angularVelocity,
                    forearm.worldCenterOfMass - previousCentre);
        }

        internal void EnableDropped()
        {
            DisableHeld();
            if (weapon == null) return;
            PlaceShapes(dropped);
            SetEnabled(dropped, true);
            IsDropped = true;
            CaptureDroppedSnapshot();
            // A released handle may still touch the old fingers. Restore every
            // pair after separation or this short bound, even if still overlapping.
            AllowInitialContact(dropped, rightContact);
            AllowInitialContact(dropped, leftContact);
        }

        internal void ResetWeapon()
        {
            DisableHeld();
            SetEnabled(dropped, false);
            IsDropped = false;
            hasDroppedSnapshot = false;
            DroppedAnatomyContactCount = 0;
        }

        private CapsuleCollider[] CreateShapes(Transform parent, string label)
        {
            var result = new CapsuleCollider[CombatWeaponGeometry.Segments.Count];
            for (int i = 0; i < result.Length; i++)
            {
                var proxy = new GameObject(label + " " + i);
                proxy.layer = weapon.gameObject.layer;
                proxy.transform.SetParent(parent, false);
                var collider = proxy.AddComponent<CapsuleCollider>();
                collider.enabled = false;
                collider.direction = 1;
                collider.contactOffset = .004f;
                result[i] = collider;
            }
            PlaceShapes(result);
            return result;
        }

        private void PlaceShapes(CapsuleCollider[] colliders)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                CapsuleCollider collider = colliders[i];
                if (collider == null) continue;
                CombatWeaponGeometry.Segment segment = CombatWeaponGeometry.Segments[i];
                Vector3 a = weapon.position + weapon.rotation * segment.A;
                Vector3 b = weapon.position + weapon.rotation * segment.B;
                Vector3 axis = b - a;
                Transform proxy = collider.transform;
                Vector3 scale = proxy.parent.lossyScale;
                proxy.localScale = new Vector3(1f / Mathf.Max(.00001f, Mathf.Abs(scale.x)),
                    1f / Mathf.Max(.00001f, Mathf.Abs(scale.y)), 1f / Mathf.Max(.00001f, Mathf.Abs(scale.z)));
                proxy.SetPositionAndRotation((a + b) * .5f, axis.sqrMagnitude > .000001f
                    ? Quaternion.FromToRotation(Vector3.up, axis.normalized) : weapon.rotation);
                collider.center = Vector3.zero;
                collider.radius = segment.Radius;
                collider.height = axis.magnitude + segment.Radius * 2f;
            }
        }

        private void AllowInitialContact(CapsuleCollider[] colliders, List<Collider> contacts)
        {
            foreach (CapsuleCollider collider in colliders)
                foreach (Collider contact in contacts)
                {
                    if (contact == null || !contact.enabled || !Overlapping(collider, contact)) continue;
                    Physics.IgnoreCollision(collider, contact, true);
                    releasePairs.Add(new ReleasePair { Weapon = collider, Hand = contact, Remaining = ReleaseContactSeconds });
                }
        }

        private static bool Overlapping(Collider a, Collider b) => Physics.ComputePenetration(a,
            a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out _);

        private void FixedUpdate()
        {
            if (Time.timeScale <= 0f || PauseMenuController.IsAnyPaused ||
                (IsHeld && (forearm == null || forearm.isKinematic)) ||
                (IsDropped && (weaponBody == null || weaponBody.isKinematic))) return;
            ResolveDroppedAnatomy();
            for (int i = releasePairs.Count - 1; i >= 0; i--)
            {
                ReleasePair pair = releasePairs[i];
                pair.Remaining -= Time.fixedDeltaTime;
                if (pair.Weapon != null && pair.Hand != null && pair.Remaining > 0f && Overlapping(pair.Weapon, pair.Hand))
                { releasePairs[i] = pair; continue; }
                if (pair.Weapon != null && pair.Hand != null) Physics.IgnoreCollision(pair.Weapon, pair.Hand, false);
                releasePairs.RemoveAt(i);
            }
        }

        private void LateUpdate()
        {
            // Animation has now presented the live limbs. Consume this movement
            // through the same sweep as physics, before recording a new snapshot.
            if (Time.timeScale > 0f && !PauseMenuController.IsAnyPaused) ResolveDroppedAnatomy();
        }

        private void CaptureDroppedSnapshot()
        {
            if (weaponBody == null) return;
            previousDropped = new Pose(weaponBody.position, weaponBody.rotation);
            for (int i = 0; i < opponentAnatomy.Count; i++)
                if (opponentAnatomy[i] != null)
                    previousAnatomy[i] = new Pose(opponentAnatomy[i].transform.position, opponentAnatomy[i].transform.rotation);
            hasDroppedSnapshot = true;
        }

        internal void ResolveDroppedAnatomy()
        {
            if (!IsDropped || weaponBody == null || weaponBody.isKinematic) return;
            if (!hasDroppedSnapshot) { CaptureDroppedSnapshot(); return; }
            Pose target = new Pose(weaponBody.position, weaponBody.rotation);
            float travel = Vector3.Distance(previousDropped.position, target.position) +
                Quaternion.Angle(previousDropped.rotation, target.rotation) * Mathf.Deg2Rad * .7f;
            bool hasAnimatedBody = false;
            for (int i = 0; i < opponentAnatomy.Count; i++)
            {
                Collider shape = opponentAnatomy[i];
                if (shape == null) continue;
                currentAnatomy[i] = new Pose(shape.transform.position, shape.transform.rotation);
                if (shape.enabled || !shape.gameObject.activeInHierarchy) continue;
                hasAnimatedBody = true;
                travel = Mathf.Max(travel, Vector3.Distance(previousAnatomy[i].position, currentAnatomy[i].position) +
                    Quaternion.Angle(previousAnatomy[i].rotation, currentAnatomy[i].rotation) * Mathf.Deg2Rad * ShapeRadius(shape) +
                    Vector3.Distance(previousDropped.position, target.position) +
                    Quaternion.Angle(previousDropped.rotation, target.rotation) * Mathf.Deg2Rad * .7f);
            }
            if (!hasAnimatedBody) { CaptureDroppedSnapshot(); return; }
            // FixedUpdate and LateUpdate expose different time clocks. A frame's
            // authored limb motion must not acquire a near-zero denominator
            // merely because a fixed step captured its previous pose just before it.
            float seconds = Mathf.Max(Time.fixedDeltaTime, Time.deltaTime);
            // Subdivide relative surface travel, including a moving limb crossing
            // an otherwise stationary prop. No global ragdoll collider is enabled.
            int steps = Mathf.Max(1, Mathf.CeilToInt(travel / .008f));
            Pose solved = previousDropped;
            Vector3 slide = Vector3.zero;
            bool touched = false;
            for (int step = 1; step <= steps; step++)
            {
                float t = step / (float)steps;
                Vector3 planned = Vector3.Lerp(previousDropped.position, target.position, t);
                Pose next = new Pose(planned + slide, Quaternion.Slerp(previousDropped.rotation, target.rotation, t));
                if (touched)
                {
                    next.position = solved.position + LimitWorldCorrection(solved, next.position - solved.position);
                    if (!WorldEndpointClear(solved, next)) next.rotation = solved.rotation;
                }
                for (int iteration = 0; iteration < 8; iteration++)
                {
                    if (!FindAnimatedContact(next, t, out Vector3 normal, out float depth, out Vector3 point, out int part)) break;
                    touched = true;
                    DroppedAnatomyContactCount++;
                    LastDroppedAnatomyPart = opponentParts[part];
                    Pose bodyPose = Interpolate(previousAnatomy[part], currentAnatomy[part], t);
                    Vector3 localContact = Quaternion.Inverse(bodyPose.rotation) * (point - bodyPose.position);
                    Vector3 bodyVelocity = (currentAnatomy[part].position + currentAnatomy[part].rotation * localContact -
                        previousAnatomy[part].position - previousAnatomy[part].rotation * localContact) / seconds;
                    RemoveClosingVelocity(point, normal, bodyVelocity);
                    Vector3 correction = LimitWorldCorrection(next, normal * (depth + DroppedSkin));
                    if (correction.sqrMagnitude < .00000001f)
                    {
                        // A wall cannot be crossed to satisfy a moving body. Stop
                        // at the preceding pose instead of choosing the far side.
                        next = solved;
                        break;
                    }
                    next.position += correction;
                }
                solved = next;
                slide = solved.position - planned;
            }
            if (touched)
            {
                weaponBody.position = solved.position;
                weaponBody.rotation = solved.rotation;
                // Interpolation must not display the rejected physics pose once
                // the contact pass has put the body back on its near surface.
                weapon.SetPositionAndRotation(solved.position, solved.rotation);
            }
            CaptureDroppedSnapshot();
        }

        private bool FindAnimatedContact(Pose pose, float t, out Vector3 normal, out float depth,
            out Vector3 point, out int bodyIndex)
        {
            normal = point = Vector3.zero; depth = 0f; bodyIndex = -1;
            for (int segment = 0; segment < dropped.Length; segment++)
            {
                SegmentPose(pose, segment, out Vector3 a, out Vector3 b, out Quaternion rotation);
                for (int body = 0; body < opponentAnatomy.Count; body++)
                {
                    Collider shape = opponentAnatomy[body];
                    if (shape == null || shape.enabled || !shape.gameObject.activeInHierarchy) continue;
                    Pose bodyPose = Interpolate(previousAnatomy[body], currentAnatomy[body], t);
                    if (Vector3.Distance((a + b) * .5f, bodyPose.position) >
                        ShapeRadius(shape) + Vector3.Distance(a, b) * .5f + dropped[segment].radius) continue;
                    if (!Physics.ComputePenetration(dropped[segment], (a + b) * .5f, rotation,
                        shape, bodyPose.position, bodyPose.rotation, out Vector3 direction, out float overlap) || overlap <= depth) continue;
                    depth = overlap; normal = direction; bodyIndex = body;
                    Vector3 line = b - a;
                    float along = line.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector3.Dot(bodyPose.position - a, line) / line.sqrMagnitude) : 0f;
                    point = Vector3.Lerp(a, b, along) - direction * dropped[segment].radius;
                }
            }
            return bodyIndex >= 0;
        }

        private Vector3 LimitWorldCorrection(Pose pose, Vector3 correction)
        {
            float length = correction.magnitude;
            if (length < .000001f) return Vector3.zero;
            float allowed = length;
            for (int segment = 0; segment < dropped.Length; segment++)
            {
                SegmentPose(pose, segment, out Vector3 a, out Vector3 b, out _);
                int count = Physics.CapsuleCastNonAlloc(a, b, dropped[segment].radius, correction / length,
                    droppedCasts, length + DroppedSkin, ~0, QueryTriggerInteraction.Ignore);
                if (count == droppedCasts.Length) return Vector3.zero;
                for (int hit = 0; hit < count; hit++)
                    if (SolidForDropped(dropped[segment], droppedCasts[hit].collider))
                        allowed = Mathf.Min(allowed, Mathf.Max(0f, droppedCasts[hit].distance - DroppedSkin));
            }
            Vector3 candidate = correction * (allowed / length);
            Pose end = new Pose(pose.position + candidate, pose.rotation);
            // Casts may begin in a contact skin. Reject only a new/deeper world
            // penetration, so sliding out of a resting floor contact remains legal.
            if (!WorldEndpointClear(pose, end)) return Vector3.zero;
            return candidate;
        }

        private bool WorldEndpointClear(Pose from, Pose to)
        {
            for (int segment = 0; segment < dropped.Length; segment++)
            {
                SegmentPose(to, segment, out Vector3 a, out Vector3 b, out Quaternion rotation);
                SegmentPose(from, segment, out Vector3 oldA, out Vector3 oldB, out Quaternion oldRotation);
                int count = Physics.OverlapCapsuleNonAlloc(a, b, dropped[segment].radius, droppedOverlaps,
                    ~0, QueryTriggerInteraction.Ignore);
                if (count == droppedOverlaps.Length) return false;
                for (int i = 0; i < count; i++)
                {
                    Collider shape = droppedOverlaps[i];
                    if (SolidForDropped(dropped[segment], shape) && Physics.ComputePenetration(dropped[segment],
                        (a + b) * .5f, rotation, shape, shape.transform.position, shape.transform.rotation, out _, out float overlap) &&
                        overlap > .001f)
                    {
                        Physics.ComputePenetration(dropped[segment], (oldA + oldB) * .5f, oldRotation,
                            shape, shape.transform.position, shape.transform.rotation, out _, out float previousOverlap);
                        if (overlap > previousOverlap + .0001f) return false;
                    }
                }
            }
            return true;
        }

        private bool SolidForDropped(Collider source, Collider target) => target != null && target.enabled &&
            !target.isTrigger && target.attachedRigidbody != weaponBody && !Physics.GetIgnoreCollision(source, target) &&
            !Physics.GetIgnoreLayerCollision(source.gameObject.layer, target.gameObject.layer);

        private static Pose Interpolate(Pose a, Pose b, float t) =>
            new Pose(Vector3.Lerp(a.position, b.position, t), Quaternion.Slerp(a.rotation, b.rotation, t));

        private static float ShapeRadius(Collider shape)
        {
            Vector3 scale = shape.transform.lossyScale;
            float factor = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            if (shape is CapsuleCollider capsule) return (capsule.center.magnitude + Mathf.Max(capsule.height * .5f, capsule.radius)) * factor;
            if (shape is BoxCollider box) return (box.center.magnitude + box.size.magnitude * .5f) * factor;
            return shape.bounds.extents.magnitude;
        }

        private static void SegmentPose(Pose weaponPose, int index, out Vector3 a, out Vector3 b, out Quaternion rotation)
        {
            CombatWeaponGeometry.Segment segment = CombatWeaponGeometry.Segments[index];
            a = weaponPose.position + weaponPose.rotation * segment.A;
            b = weaponPose.position + weaponPose.rotation * segment.B;
            rotation = (b - a).sqrMagnitude > .000001f ? Quaternion.FromToRotation(Vector3.up, b - a) : weaponPose.rotation;
        }

        private void RemoveClosingVelocity(Vector3 point, Vector3 normal, Vector3 surfaceVelocity)
        {
            float closing = Vector3.Dot(weaponBody.GetPointVelocity(point) - surfaceVelocity, normal);
            if (closing >= 0f) return;
            Vector3 lever = point - weaponBody.worldCenterOfMass;
            Quaternion inertiaRotation = weaponBody.rotation * weaponBody.inertiaTensorRotation;
            Vector3 torqueAxis = Quaternion.Inverse(inertiaRotation) * Vector3.Cross(lever, normal);
            Vector3 inertia = weaponBody.inertiaTensor;
            Vector3 inverseTorque = inertiaRotation * new Vector3(torqueAxis.x / Mathf.Max(.00001f, inertia.x),
                torqueAxis.y / Mathf.Max(.00001f, inertia.y), torqueAxis.z / Mathf.Max(.00001f, inertia.z));
            float inverseMass = 1f / weaponBody.mass;
            float impulse = -closing / Mathf.Max(.00001f, inverseMass + Vector3.Dot(normal, Vector3.Cross(inverseTorque, lever)));
            weaponBody.linearVelocity += normal * (impulse * inverseMass);
            weaponBody.angularVelocity += inverseTorque * impulse;
        }

        private void ClearReleasePairs()
        {
            foreach (ReleasePair pair in releasePairs)
                if (pair.Weapon != null && pair.Hand != null) Physics.IgnoreCollision(pair.Weapon, pair.Hand, false);
            releasePairs.Clear();
        }

        private static void SetEnabled(CapsuleCollider[] colliders, bool value)
        { foreach (CapsuleCollider collider in colliders) if (collider != null) collider.enabled = value; }

        internal void Dispose()
        {
            if (IsDropped && weaponBody != null)
            { weaponBody.isKinematic = true; weaponBody.useGravity = false; weaponBody.detectCollisions = false; }
            ResetWeapon();
            foreach (CapsuleCollider collider in held) if (collider != null) Destroy(collider.gameObject);
            foreach (CapsuleCollider collider in dropped) if (collider != null) Destroy(collider.gameObject);
            held = dropped = Array.Empty<CapsuleCollider>();
        }

        private void OnDisable()
        {
            // The detached prop can outlive an actor being deactivated. Never
            // leave a dynamic body moving after its owned collision was removed.
            if (IsDropped && weaponBody != null)
            { weaponBody.isKinematic = true; weaponBody.useGravity = false; weaponBody.detectCollisions = false; }
            ResetWeapon();
        }
        private void OnDestroy() => Dispose();
    }
}
