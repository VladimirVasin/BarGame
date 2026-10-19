using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private static readonly string[] CraneLooseBones = { "upper_arm.L", "forearm.L", "upper_arm.R", "forearm.R", "thigh.L", "shin.L", "thigh.R", "shin.R" };
        private static readonly string[] CraneLooseTips = { "forearm.L", "hand.L", "forearm.R", "hand.R", "shin.L", "foot.L", "shin.R", "foot.R" };
        private readonly Transform[] craneLooseBones = new Transform[8], craneLooseTips = new Transform[8];
        private readonly Quaternion[] craneAuthoredJointRotations = new Quaternion[8];
        private readonly Vector3[] craneJointAngles = new Vector3[8], craneJointSpeeds = new Vector3[8], craneRestDirections = new Vector3[8];
        private Vector3 craneAcceleration, cranePreviousVelocity;
        public Vector3 CraneSwingOffset => CraneLoadOffset;
        public float CraneLimbMotionEnergy { get; private set; }
        private bool CraneAiming => !IsCraneFrenzied &&
            CranePhase is CombatCranePhase.Signal or CombatCranePhase.Striking or CombatCranePhase.Capturing or CombatCranePhase.Recovering;
        private float CranePendulumLength => Mathf.Clamp(CraneAnchorPosition.y - craneCommand.y - 1.15f, .65f, 8f);
        private Vector3 CraneLoadOffset
        {
            get
            {
                float length = CranePendulumLength;
                return craneSway + Vector3.up * (length - Mathf.Sqrt(Mathf.Max(.01f, length * length - craneSway.sqrMagnitude)));
            }
        }
        private Vector3 CraneLoadPosition => craneCommand + CraneLoadOffset;

        private void InitializeCraneDynamics()
        {
            for (int i = 0; i < craneLooseBones.Length; i++)
            { craneLooseBones[i] = RequireCraneBone(CraneLooseBones[i]); craneLooseTips[i] = RequireCraneBone(CraneLooseTips[i]); }
        }

        private void ResetCraneDynamics()
        {
            Array.Clear(craneJointAngles, 0, craneJointAngles.Length); Array.Clear(craneJointSpeeds, 0, craneJointSpeeds.Length);
            craneAcceleration = cranePreviousVelocity = Vector3.zero; CraneLimbMotionEnergy = 0f;
            Array.Clear(craneRopeSlack, 0, craneRopeSlack.Length);
        }

        private void AdvanceCraneDynamics(float seconds, Vector3 drivenVelocity)
        {
            craneAcceleration = Vector3.ClampMagnitude((drivenVelocity - cranePreviousVelocity) / seconds, 18f);
            cranePreviousVelocity = drivenVelocity; craneCommandVelocity = drivenVelocity;
            float length = CranePendulumLength;
            Vector3 horizontalAcceleration = Vector3.ProjectOnPlane(craneAcceleration, Vector3.up);
            craneSwayVelocity += (-craneSway * (9.81f / length) - craneSwayVelocity * .95f - horizontalAcceleration) * seconds;
            craneSway = Vector3.ClampMagnitude(craneSway + craneSwayVelocity * seconds, Mathf.Min(.78f, length * .46f));
            craneSway.x = Mathf.Clamp(craneSway.x, -5.15f - craneCommand.x, 5.15f - craneCommand.x);
            craneSway.z = Mathf.Clamp(craneSway.z, -5.15f - craneCommand.z, 5.15f - craneCommand.z); craneSway.y = 0f;
            CraneLimbMotionEnergy = 0f;
            for (int i = 0; i < craneJointAngles.Length; i++)
            {
                bool controlled = CraneAiming && i is 2 or 3;
                Vector3 direction = craneRestDirections[i].sqrMagnitude > .1f ? craneRestDirections[i] : Vector3.down;
                Vector3 torque = Vector3.Cross(direction, Vector3.down * 9.81f - craneAcceleration * (1.2f + i * .09f));
                float spring = controlled ? 55f : i < 4 ? 7f : 10f;
                craneJointSpeeds[i] += (torque * (controlled ? 0f : 70f) - craneJointAngles[i] * spring - craneJointSpeeds[i] * (controlled ? 12f : 2.1f)) * seconds;
                craneJointSpeeds[i] = Vector3.ClampMagnitude(craneJointSpeeds[i], 260f);
                craneJointAngles[i] = Vector3.ClampMagnitude(craneJointAngles[i] + craneJointSpeeds[i] * seconds, controlled ? 8f : i < 4 ? 42f : 32f);
                CraneLimbMotionEnergy += craneJointSpeeds[i].sqrMagnitude;
            }
        }

        private void ApplyCraneDynamicPose()
        {
            float length = CranePendulumLength;
            Vector3 tilt = new Vector3(craneSway.z, 0f, -craneSway.x) * (Mathf.Rad2Deg / length * .7f);
            craneClearanceBones[0].rotation = Quaternion.Euler(tilt) * craneClearanceBones[0].rotation;
            // Retain the authored insect folds. The mechanical cuff swivels
            // toward the shot; human reach IK distorts this stretched chain.
            RefreshCraneAttachmentSockets();
            for (int i = 0; i < craneAttachments.Length; i++)
                craneTetherLengths[i] = Vector3.Distance(craneFeeds[i].position, craneAttachments[i].position);
            for (int i = 0; i < craneLooseBones.Length; i++)
            {
                craneRestDirections[i] = (craneLooseTips[i].position - craneLooseBones[i].position).normalized;
                craneAuthoredJointRotations[i] = craneLooseBones[i].localRotation;
            }
            for (int i = 0; i < craneLooseBones.Length; i++)
            {
                if (CraneAiming && i is 2 or 3) continue;
                craneLooseBones[i].rotation = Quaternion.Euler(craneJointAngles[i]) * craneLooseBones[i].rotation;
            }
            // Each wrist/ankle is held by its own ceiling cable. A passive limb
            // may swing inward and unload, but cannot fall beyond its payout.
            ConstrainCraneLimb(0, 0, 1); ConstrainCraneLimb(1, 2, 3);
            ConstrainCraneLimb(3, 4, 5); ConstrainCraneLimb(4, 6, 7);
            for (int i = 0; i < craneAttachments.Length; i++)
            {
                float actual = Vector3.Distance(craneFeeds[i].position, craneAttachments[i].position);
                craneRopeSlack[i] = Mathf.Clamp(craneTetherLengths[i] - actual, 0f, .16f);
            }
        }

        private void ConstrainCraneLimb(int cable, int upperIndex, int lowerIndex)
        {
            Transform tip = craneLooseTips[lowerIndex], upper = craneLooseBones[upperIndex], lower = craneLooseBones[lowerIndex];
            Vector3 feed = craneFeeds[cable].position, delta = tip.position - feed;
            float maximum = craneTetherLengths[cable] + .015f;
            if (delta.sqrMagnitude <= maximum * maximum && tip.position.y < feed.y - .03f) return;
            // Blend inertial rotations back toward the authored folded chain.
            // Reach IK on its stretched, nonuniform parents can change segment
            // lengths and fling a wrist/ankle above the suspension cassette.
            Quaternion upperDriven = upper.localRotation, lowerDriven = lower.localRotation;
            float accepted = 0f, rejected = 1f;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                float weight = (accepted + rejected) * .5f;
                upper.localRotation = Quaternion.Slerp(craneAuthoredJointRotations[upperIndex], upperDriven, weight);
                lower.localRotation = Quaternion.Slerp(craneAuthoredJointRotations[lowerIndex], lowerDriven, weight);
                if ((tip.position - feed).sqrMagnitude <= maximum * maximum && tip.position.y < feed.y - .03f)
                    accepted = weight;
                else rejected = weight;
            }
            upper.localRotation = Quaternion.Slerp(craneAuthoredJointRotations[upperIndex], upperDriven, accepted);
            lower.localRotation = Quaternion.Slerp(craneAuthoredJointRotations[lowerIndex], lowerDriven, accepted);
            craneJointSpeeds[upperIndex] *= .75f; craneJointSpeeds[lowerIndex] *= .75f;
        }

        private Vector3 CraneRopePoint(int index, float t) => CraneRopePoint(index, t, craneFeeds[index].position, craneAttachments[index].position);

        private void RefreshCraneAttachmentSockets()
        {
            // Imported clips can change a head's scale. Its crown attachment
            // is authored in metres, so a bind-time local offset cannot own it.
            for (int i = 0; i < craneAttachments.Length; i++)
            {
                Vector3 offset = CombatCraneAssetProvider.SuspensionLocalOffsets[i];
                if (offset.sqrMagnitude <= 0f) continue;
                Transform socket = craneAttachments[i], bone = socket.parent;
                socket.position = bone.position + bone.rotation * offset;
            }
        }
        private Vector3 CraneRopePoint(int index, float t, Vector3 feed, Vector3 end)
        {
            float bow = Mathf.Min(.16f, Mathf.Sqrt(craneRopeSlack[index] * Mathf.Max(.1f, Vector3.Distance(feed, end))) * .35f);
            Vector3 transverse = Vector3.ProjectOnPlane(Vector3.down, (end - feed).normalized);
            if (transverse.sqrMagnitude < .01f) transverse = index % 2 == 0 ? Vector3.right : Vector3.forward;
            return Vector3.Lerp(feed, end, t) + transverse.normalized * (Mathf.Sin(t * Mathf.PI) * bow);
        }

        private void PresentCraneRopes()
        {
            for (int i = 0; i < craneAttachments.Length; i++)
                for (int segment = 0; segment < CraneCableSegments; segment++)
                {
                    Vector3 from = GetCraneCableSegmentStart(i, segment), to = GetCraneCableSegmentEnd(i, segment);
                    Transform piece = craneCablePieces[i, segment];
                    piece.SetPositionAndRotation(from, Quaternion.FromToRotation(Vector3.up, to - from));
                    Vector3 scale = craneCableScales[i, segment]; scale.y *= Vector3.Distance(from, to); piece.localScale = scale;
                }
        }

        private void SetCraneRopesVisible(bool visible)
        {
            for (int i = 0; i < craneAttachments.Length; i++)
                for (int segment = 0; segment < CraneCableSegments; segment++) craneCablePieces[i, segment].gameObject.SetActive(visible);
        }
    }
}
