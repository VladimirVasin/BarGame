using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The left palm closes on the right-hand-owned weapon after all pose blends.</summary>
    public sealed class CombatSupportGrip
    {
        public const float ReadyDistance = .16f, BlockDistance = .42f;
        private const float RegripSeconds = .18f;
        private readonly Transform frame, weapon, upper, forearm, hand, socket;
        private readonly NpcHandPose hands;
        private Quaternion upperBase, forearmBase, handBase;
        private bool applied, initialized, wantsSupport = true;
        private float distance = ReadyDistance, startDistance = ReadyDistance, targetDistance = ReadyDistance;
        private float elapsed = RegripSeconds, weight = 1f;

        public float Weight => initialized ? weight : 0f;
        public Vector3 Target => weapon != null ? weapon.TransformPoint(new Vector3(0f, distance, 0f)) : Vector3.zero;

        public CombatSupportGrip(Transform rig, Transform actorFrame, NpcHandPose handPose, Transform heldWeapon)
        {
            frame = actorFrame; hands = handPose; weapon = heldWeapon;
            foreach (Transform bone in rig.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "upper_arm.L") upper = bone;
                else if (bone.name == "forearm.L") forearm = bone;
                else if (bone.name == "hand.L") hand = bone;
            }
            foreach (NpcHandPose.HandBinding binding in hands.Hands)
                if (binding.IsLeft) socket = binding.GripSocket;
            if (upper == null || forearm == null || hand == null || socket == null)
                throw new InvalidOperationException("Combat support requires the original left arm and cylindrical palm.");
        }

        public void SetTarget(bool blocking, bool supported)
        {
            float next = blocking ? BlockDistance : ReadyDistance;
            if (!initialized)
            {
                initialized = true; distance = startDistance = targetDistance = next;
                weight = supported ? 1f : 0f;
            }
            if (!Mathf.Approximately(next, targetDistance))
            { startDistance = distance; targetDistance = next; elapsed = 0f; }
            wantsSupport = supported;
        }

        public void Advance(float seconds)
        {
            elapsed = Mathf.Min(RegripSeconds, elapsed + seconds);
            distance = Mathf.Lerp(startDistance, targetDistance, Mathf.SmoothStep(0f, 1f, elapsed / RegripSeconds));
            weight = Mathf.MoveTowards(weight, wantsSupport ? 1f : 0f, seconds / RegripSeconds);
        }

        public void Apply()
        {
            Restore();
            if (!initialized || weapon == null || hand == null || hands == null) return;
            // The fingers loosen while the palm travels along the shaft, then close again.
            float slide = Mathf.Sin(Mathf.PI * Mathf.Clamp01(elapsed / RegripSeconds));
            hands.SetGrip(true, weight * (1f - .28f * slide));
            if (weight <= 0f) return;
            upperBase = upper.localRotation; forearmBase = forearm.localRotation; handBase = hand.localRotation;
            applied = true;
            Vector3 axis = weapon.up;
            // The upper hand wraps the opposite side of the bar. Derive its
            // palm normal and shaft axis from the actual weapon/right hand,
            // including intermediate charge powers and regrips.
            Vector3 palm = Vector3.ProjectOnPlane(-hands.PalmNormal(false), axis).normalized;
            if (palm.sqrMagnitude < .1f) palm = Vector3.ProjectOnPlane(-frame.forward, axis).normalized;
            Pose contact = hands.GetSocketPose(true, Target, axis, palm);
            Vector3 socketOffset = Quaternion.Inverse(hand.rotation) * (socket.position - hand.position);
            Vector3 outward = -frame.right;
            Vector3 hint = upper.position - frame.up * .45f + outward * .18f - frame.forward * .035f;
            LimbTwoBoneIk.Solve(upper, forearm, hand, contact.position - contact.rotation * socketOffset,
                contact.rotation, hint, weight, .999f, true);
        }

        public void Restore()
        {
            if (!applied) return;
            if (upper != null) upper.localRotation = upperBase;
            if (forearm != null) forearm.localRotation = forearmBase;
            if (hand != null) hand.localRotation = handBase;
            applied = false;
        }

        public void Forget() => applied = false;

        public void Reset()
        {
            Restore(); initialized = false; wantsSupport = true;
            distance = startDistance = targetDistance = ReadyDistance;
            elapsed = RegripSeconds; weight = 1f;
            if (hands != null) hands.SetGrip(true, 0f);
        }
    }
}
