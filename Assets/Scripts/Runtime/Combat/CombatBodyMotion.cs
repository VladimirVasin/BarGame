using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Combat-only weight transfer. The duel advances it; pose/contact samples only read it.</summary>
    internal sealed class CombatBodyMotion
    {
        private readonly Transform frame;
        private readonly Transform[] bones = new Transform[4];
        private readonly Quaternion[] baseRotations = new Quaternion[4];
        private SecondOrderFilter forwardSpeed = new SecondOrderFilter(12f, 1f);
        private SecondOrderFilter sideSpeed = new SecondOrderFilter(12f, 1f);
        private SecondOrderFilter pitch = new SecondOrderFilter(15f, .85f);
        private SecondOrderFilter roll = new SecondOrderFilter(15f, .85f);
        private SecondOrderFilter turn = new SecondOrderFilter(14f, 1f);
        private SecondOrderFilter headPitch = new SecondOrderFilter(11f, .85f);
        private SecondOrderFilter headRoll = new SecondOrderFilter(11f, .85f);
        private Vector3 previousForward;
        private bool applied;

        public CombatBodyMotion(Transform root, Transform actorFrame)
        {
            frame = actorFrame;
            string[] names = { "spine", "chest", "neck", "head" };
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
                for (int i = 0; i < names.Length; i++)
                    if (bone.name == names[i]) bones[i] = bone;
            foreach (Transform bone in bones)
                if (bone == null) throw new InvalidOperationException("Combat motion needs the original torso and head chain.");
            Reset();
        }

        public void Advance(float seconds, Vector3 velocity)
        {
            if (seconds <= 0f) return;
            Vector3 local = frame.InverseTransformDirection(velocity);
            local.y = 0f;
            local = Vector3.ClampMagnitude(local, 5f);
            // The difference from the lagging centre of mass changes sign when braking.
            float forward = local.z - forwardSpeed.Value;
            float sideways = local.x - sideSpeed.Value;
            forwardSpeed.Advance(local.z, seconds);
            sideSpeed.Advance(local.x, seconds);
            float yaw = Mathf.Clamp(Vector3.SignedAngle(previousForward, frame.forward, Vector3.up) / seconds, -240f, 240f);
            previousForward = frame.forward;
            pitch.Advance(Mathf.Clamp(forward * 3.5f + local.z * .6f, -6f, 6f), seconds);
            roll.Advance(Mathf.Clamp(-sideways * 4f - local.x * .7f, -6f, 6f), seconds);
            turn.Advance(-yaw * .022f, seconds);
            headPitch.Advance(-pitch.Value * .45f, seconds);
            headRoll.Advance(-roll.Value * .45f, seconds);
        }

        public void Apply()
        {
            Restore();
            for (int i = 0; i < bones.Length; i++) baseRotations[i] = bones[i].localRotation;
            applied = true;
            // Leave pelvis/soles on their authored contacts. The hands inherit
            // the chest, then the existing support solver closes the left palm.
            Rotate(0, frame.right, pitch.Value * .35f);
            Rotate(1, frame.right, pitch.Value * .65f);
            Rotate(0, frame.forward, roll.Value * .35f);
            Rotate(1, frame.forward, roll.Value * .65f);
            Rotate(1, Vector3.up, turn.Value);
            Rotate(2, frame.right, headPitch.Value * .35f);
            Rotate(3, frame.right, headPitch.Value * .65f);
            Rotate(3, frame.forward, headRoll.Value);
        }

        public void Restore()
        {
            if (!applied) return;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].localRotation = baseRotations[i];
            applied = false;
        }

        public void Forget() => applied = false;

        public void Reset()
        {
            Restore();
            forwardSpeed.Reset(); sideSpeed.Reset(); pitch.Reset(); roll.Reset(); turn.Reset();
            headPitch.Reset(); headRoll.Reset();
            previousForward = frame.forward;
        }

        private void Rotate(int index, Vector3 axis, float degrees) =>
            bones[index].rotation = Quaternion.AngleAxis(degrees, axis) * bones[index].rotation;
    }
}
