using UnityEngine;

namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private object sleepBodyOwner;
        private float sleepNightmare, sleepRecovery, sleepStartle, sleepSeatedIdle, sleepBodySeconds;
        private bool sleepBodyApplied;
        private Quaternion sleepChestBase, sleepNeckBase, sleepHeadBase;
        private Quaternion sleepLeftUpperBase, sleepLeftForearmBase, sleepLeftHandBase;
        private Quaternion sleepRightUpperBase, sleepRightForearmBase, sleepRightHandBase;

        /// <summary>The shared bed clock supplies time and envelopes; this layer never advances an action.</summary>
        public bool TrySetSleepBodyPresentation(object owner, float nightmare01, float recovery01,
            float elapsedSeconds, float startle01 = 0f, float seatedIdle01 = 0f)
        {
            if (owner == null || !isActiveAndEnabled || registry == null || headBone == null ||
                neckBone == null || chestBone == null || ragdollPoseActive ||
                !float.IsFinite(nightmare01) || !float.IsFinite(recovery01) ||
                !float.IsFinite(elapsedSeconds) || !float.IsFinite(startle01) || !float.IsFinite(seatedIdle01) ||
                (sleepBodyOwner != null && !ReferenceEquals(sleepBodyOwner, owner))) return false;
            sleepBodyOwner = owner;
            sleepNightmare = Mathf.Clamp01(nightmare01);
            sleepRecovery = Mathf.Clamp01(recovery01);
            sleepStartle = Mathf.Clamp01(startle01);
            sleepSeatedIdle = Mathf.Clamp01(seatedIdle01);
            sleepBodySeconds = Mathf.Max(0f, elapsedSeconds);
            return true;
        }

        public void ReleaseSleepBodyPresentation(object owner)
        {
            if (owner == null || !ReferenceEquals(sleepBodyOwner, owner)) return;
            RestoreSleepBodyPresentation();
            sleepBodyOwner = null;
            sleepNightmare = sleepRecovery = sleepStartle = sleepSeatedIdle = sleepBodySeconds = 0f;
        }

        private void ApplySleepBodyPresentation()
        {
            RestoreSleepBodyPresentation();
            if (sleepBodyOwner == null || !isActiveAndEnabled || ragdollPoseActive || !IsClipActive ||
                sleepNightmare + sleepRecovery + sleepStartle + sleepSeatedIdle <= .00001f) return;
            sleepChestBase = chestBone.localRotation;
            sleepNeckBase = neckBone.localRotation;
            sleepHeadBase = headBone.localRotation;
            sleepLeftUpperBase = LocalRotation(leftUpperArmBone);
            sleepLeftForearmBase = LocalRotation(leftForearmBone);
            sleepLeftHandBase = LocalRotation(leftHandBone);
            sleepRightUpperBase = LocalRotation(rightUpperArmBone);
            sleepRightForearmBase = LocalRotation(rightForearmBone);
            sleepRightHandBase = LocalRotation(rightHandBone);
            sleepBodyApplied = true;

            Vector3 leftTarget = leftHandBone != null ? leftHandBone.position : Vector3.zero;
            Vector3 rightTarget = rightHandBone != null ? rightHandBone.position : Vector3.zero;
            Quaternion leftRotation = leftHandBone != null ? leftHandBone.rotation : Quaternion.identity;
            Quaternion rightRotation = rightHandBone != null ? rightHandBone.rotation : Quaternion.identity;
            Vector3 leftHint = leftForearmBone != null ? leftForearmBone.position : Vector3.zero;
            Vector3 rightHint = rightForearmBone != null ? rightForearmBone.position : Vector3.zero;
            Transform frame = actorFacingTransform != null ? actorFacingTransform : transform;
            Vector3 right = headPitchProbeCaptured && neckBone.parent != null
                ? neckBone.parent.TransformDirection(headPitchProbeParentRight).normalized : frame.right;
            Vector3 forward = headPitchProbeCaptured && neckBone.parent != null
                ? neckBone.parent.TransformDirection(headPitchProbeParentForward).normalized : frame.forward;
            Vector3 up = Vector3.Cross(forward, right).normalized;

            float breath = Mathf.Sin(sleepBodySeconds * 4.2f);
            float shudder = Mathf.Sin(sleepBodySeconds * 18f);
            float idleChest = 0f, idlePitch = 0f, idleYaw = 0f, idleRoll = 0f;
            if (sleepSeatedIdle > .00001f)
            {
                // A small, slower seated breath over the exact held BedExit
                // landmark. The caller eases this envelope and owns its clock;
                // no local timer, lower-body pose or extra action is advanced.
                float seatedBreath = Mathf.Sin(sleepBodySeconds * 1.85f);
                idleChest = sleepSeatedIdle * .7f * seatedBreath;
                idlePitch = sleepSeatedIdle * (3f + .55f *
                    Mathf.Sin(sleepBodySeconds * 1.85f - .4f));
                idleYaw = sleepSeatedIdle * .3f * Mathf.Sin(sleepBodySeconds * .71f);
                idleRoll = sleepSeatedIdle * .1f * Mathf.Sin(sleepBodySeconds * .93f);
            }
            float chestPitch = sleepNightmare * (.45f * breath + .65f * shudder) +
                sleepRecovery * .45f * breath + sleepStartle * 1.2f + idleChest;
            chestBone.rotation = Quaternion.AngleAxis(chestPitch, right) * chestBone.rotation;
            // Every term lifts/tucks the lying head rather than throwing its
            // occiput backwards into the pillow. Seated recovery bows forward.
            float pitch = sleepNightmare * (1.2f + .9f * shudder) + sleepStartle * 4.5f +
                sleepRecovery * (22f + .35f * breath) + idlePitch;
            float yaw = sleepNightmare * 1.2f * Mathf.Sin(sleepBodySeconds * 12.5f) + idleYaw;
            float roll = sleepNightmare * .65f * Mathf.Sin(sleepBodySeconds * 15.3f) + idleRoll;
            Quaternion neckTurn = Quaternion.AngleAxis(pitch * .55f, right) *
                Quaternion.AngleAxis(yaw * .5f, up) * Quaternion.AngleAxis(roll * .5f, forward);
            neckBone.rotation = neckTurn * neckBone.rotation;
            Quaternion headTurn = Quaternion.AngleAxis(pitch * .45f, right) *
                Quaternion.AngleAxis(yaw * .5f, up) * Quaternion.AngleAxis(roll * .5f, forward);
            headBone.rotation = headTurn * headBone.rotation;

            // Breath may move shoulders; the original palms keep their exact
            // authored world contacts through the same shared limb solver.
            LimbTwoBoneIk.Solve(leftUpperArmBone, leftForearmBone, leftHandBone,
                leftTarget, leftRotation, leftHint);
            LimbTwoBoneIk.Solve(rightUpperArmBone, rightForearmBone, rightHandBone,
                rightTarget, rightRotation, rightHint);
        }

        private void RestoreSleepBodyPresentation()
        {
            if (sleepBodyApplied)
            {
                SetLocalRotation(chestBone, sleepChestBase);
                SetLocalRotation(neckBone, sleepNeckBase);
                SetLocalRotation(headBone, sleepHeadBase);
                SetLocalRotation(leftUpperArmBone, sleepLeftUpperBase);
                SetLocalRotation(leftForearmBone, sleepLeftForearmBase);
                SetLocalRotation(leftHandBone, sleepLeftHandBase);
                SetLocalRotation(rightUpperArmBone, sleepRightUpperBase);
                SetLocalRotation(rightForearmBone, sleepRightForearmBase);
                SetLocalRotation(rightHandBone, sleepRightHandBase);
                sleepBodyApplied = false;
            }
            // OnDisable already samples its neutral graph through this seam.
            if (!isActiveAndEnabled)
            {
                sleepBodyOwner = null;
                sleepNightmare = sleepRecovery = sleepStartle = sleepSeatedIdle = sleepBodySeconds = 0f;
            }
        }

        private static Quaternion LocalRotation(Transform bone) => bone != null ? bone.localRotation : Quaternion.identity;
        private static void SetLocalRotation(Transform bone, Quaternion value) { if (bone != null) bone.localRotation = value; }
    }
}
