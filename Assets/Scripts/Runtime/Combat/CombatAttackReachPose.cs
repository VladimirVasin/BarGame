using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded distance corrections on the original rig; samples never advance state.</summary>
    internal sealed class CombatAttackReachPose
    {
        private readonly Transform frame;
        private readonly Transform[] bones = new Transform[9];
        private readonly Vector3[] positions = new Vector3[9];
        private readonly Quaternion[] rotations = new Quaternion[9];
        private MeleeBufferedAction action;
        private float distance, weight, turnWeight;
        private int strikingSide;
        private MeleeSwing swing;
        private bool applied;

        public float Distance01 => distance;

        public CombatAttackReachPose(Transform root, Transform actorFrame)
        {
            frame = actorFrame;
            string[] names = { "pelvis", "spine", "chest", "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R" };
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
                for (int i = 0; i < names.Length; i++)
                    if (bone.name == names[i]) bones[i] = bone;
            foreach (Transform bone in bones)
                if (bone == null) throw new InvalidOperationException("Attack reach requires the original torso and leg chains.");
        }

        public void SetPose(MeleeBufferedAction kind, float distance01, float amount, float turnAmount, int side, MeleeSwing weaponSwing)
        {
            action = kind;
            distance = Mathf.Clamp01(distance01);
            weight = Mathf.Clamp01(amount);
            turnWeight = Mathf.Clamp01(turnAmount);
            strikingSide = side;
            swing = weaponSwing;
        }

        public void Apply()
        {
            Restore();
            if (action == MeleeBufferedAction.None || weight <= 0f) return;
            for (int i = 0; i < bones.Length; i++)
            { positions[i] = bones[i].localPosition; rotations[i] = bones[i].localRotation; }
            applied = true;
            if (action == MeleeBufferedAction.Kick)
            {
                int chain = 3 + strikingSide * 3;
                Vector3 foot = bones[chain + 2].position;
                Quaternion rotation = bones[chain + 2].rotation;
                Vector3 knee = bones[chain + 1].position;
                // World metres preserve the imported rig's unit factor. The
                // ordinary footwork pass closes the other, supporting leg.
                bones[0].position += frame.forward * Mathf.Lerp(.01f, .09f, distance) * weight;
                // Lead with the kicking hip, mirrored for the other leg. Keep
                // the captured world foot target and the committed root heading.
                Turn(5f, 2f, strikingSide == 0 ? 1f : -1f);
                float pitch = Mathf.Lerp(1f, -7f, distance) * weight;
                Rotate(1, pitch * .4f); Rotate(2, pitch * .6f);
                Vector3 target = foot + frame.forward * Mathf.Lerp(-.08f, .07f, distance) * weight;
                LimbTwoBoneIk.Solve(bones[chain], bones[chain + 1], bones[chain + 2], target, rotation,
                    knee + frame.forward * .15f, 1f, .985f, true);
            }
            else if (action == MeleeBufferedAction.Shove)
            {
                bones[0].position += frame.forward * (.035f * distance * weight);
                Turn(3f, 3f, 1f); // The left shoulder leads the open palm.
            }
            else
            {
                bones[0].position += frame.forward * Mathf.Lerp(-.01f, .045f, distance) * weight;
                Turn(3f, 3f, swing == MeleeSwing.Forehand ? -1f : 1f);
                float pitch = Mathf.Lerp(-2f, 6f, distance) * weight;
                Rotate(1, pitch * .65f); Rotate(2, pitch * .35f);
            }
        }

        public void Restore()
        {
            if (!applied) return;
            for (int i = 0; i < bones.Length; i++)
            { bones[i].localPosition = positions[i]; bones[i].localRotation = rotations[i]; }
            applied = false;
        }

        public void Forget() => applied = false;
        public void Reset() { Restore(); action = MeleeBufferedAction.None; distance = weight = turnWeight = 0f; }
        private void Turn(float pelvisDegrees, float spineDegrees, float direction)
        {
            float amount = direction * turnWeight;
            bones[0].rotation = Quaternion.AngleAxis(pelvisDegrees * amount, frame.up) * bones[0].rotation;
            bones[1].rotation = Quaternion.AngleAxis(spineDegrees * amount, frame.up) * bones[1].rotation;
        }
        private void Rotate(int bone, float degrees) =>
            bones[bone].rotation = Quaternion.AngleAxis(degrees, frame.right) * bones[bone].rotation;
    }
}
