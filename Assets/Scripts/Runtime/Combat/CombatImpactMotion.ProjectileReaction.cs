using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatImpactMotion
    {
        // Presentation has its own finite envelope. It cannot extend the rules
        // stagger, multiply bullet momentum or create another damage transaction.
        private const float ProjectileSnapSeconds = .065f, ProjectileTailSeconds = .82f;
        private readonly Vector3[] projectileFrom = new Vector3[11], projectileTarget = new Vector3[11], projectilePose = new Vector3[11];
        private float projectileAge = ProjectileTailSeconds, projectileDrop, projectileFromDrop, projectileTargetDrop;
        private Vector3 projectileShift, projectileFromShift, projectileTargetShift;
        internal bool ProjectileReactionActive => projectileAge < ProjectileTailSeconds;
        internal float ProjectileReactionAge => projectileAge;
        internal float ProjectileReactionAmount { get; private set; }
        internal Player3DAnatomicalPart ProjectileReactionPart { get; private set; }
        internal int InjuredLegSide { get; private set; } = -1;
        internal float InjuredLegAmount => InjuredLegSide >= 0 ? ProjectileReactionAmount : 0f;
        private Vector3 ProjectileWeightShift => projectileShift;
        private float ProjectilePelvisDrop => projectileDrop;
        private float ProjectileTorsoAmount => projectilePose[0].magnitude + projectilePose[1].magnitude + projectilePose[2].magnitude;

        internal Vector3 ProjectileRotationFor(Player3DAnatomicalPart part)
        {
            int index = BoneIndex(part);
            return index < projectilePose.Length ? projectilePose[index] : Vector3.zero;
        }

        private void BeginProjectileReaction(CombatImpact impact)
        {
            Array.Copy(projectilePose, projectileFrom, projectilePose.Length);
            Array.Clear(projectileTarget, 0, projectileTarget.Length);
            projectileFromDrop = projectileDrop; projectileFromShift = projectileShift;
            projectileTargetDrop = 0f; projectileTargetShift = Vector3.zero;
            ProjectileReactionPart = impact.Part;
            int contact = BoneIndex(impact.Part);
            InjuredLegSide = contact >= 11 ? (contact >= 14 ? 1 : 0) : -1;
            Vector3 incoming = impact.Direction.sqrMagnitude > .0001f ? impact.Direction.normalized : frame.forward;
            Vector3 bend = Vector3.Cross(Vector3.up, incoming);
            if (bend.sqrMagnitude < .01f) bend = frame.right * .35f;
            float side = Vector3.Dot(impact.Point - bones[2].position, frame.right);
            Vector3 twist = Vector3.up * Mathf.Clamp(side * Vector3.Dot(incoming, frame.forward), -.22f, .22f);
            if (contact <= 2)
            {
                bool abdomen = contact <= 1;
                AddProjectileJoint(1, bend * (abdomen ? .22f : .12f) + (abdomen ? frame.right * .22f : Vector3.zero));
                AddProjectileJoint(2, bend * (abdomen ? .12f : .34f) + twist);
                AddProjectileJoint(3, -bend * .12f);
                AddProjectileJoint(4, bend * .16f);
                // Hands lag behind the struck trunk instead of travelling as
                // one rigid board. The weapon remains on its original palm.
                AddProjectileJoint(5, -bend * .10f);
                AddProjectileJoint(8, -bend * .10f);
                projectileTargetDrop = abdomen ? .035f : .012f;
            }
            else if (contact <= 4)
            {
                AddProjectileJoint(2, bend * .16f);
                AddProjectileJoint(contact, bend * .22f);
            }
            else if (contact <= 10)
            {
                int shoulder = contact <= 7 ? 5 : 8;
                int elbow = shoulder + 1, wrist = shoulder + 2;
                Vector3 distal = bones[wrist].position - bones[elbow].position;
                Vector3 fold = Vector3.Cross(distal, bones[shoulder].position - bones[elbow].position);
                if (fold.sqrMagnitude < .0001f) fold = Vector3.Cross(distal, frame.up);
                fold = fold.sqrMagnitude > .0001f ? fold.normalized : bend;
                AddProjectileJoint(shoulder, bend * (contact == shoulder ? .46f : .19f));
                AddProjectileJoint(elbow, fold * (contact == elbow ? .42f : contact == wrist ? .26f : .15f));
                AddProjectileJoint(wrist, bend * (contact == wrist ? .30f : .12f));
                AddProjectileJoint(2, bend * .07f + frame.forward * (shoulder == 5 ? -.07f : .07f));
            }
            else
            {
                float load = .5f;
                if (hasFootSupport)
                {
                    Vector3 across = Vector3.ProjectOnPlane(rightSupport - leftSupport, Vector3.up);
                    float right = across.sqrMagnitude > .001f ? Mathf.Clamp01(Vector3.Dot(CentreOfMass - leftSupport, across) / across.sqrMagnitude) : .5f;
                    load = InjuredLegSide == 0 ? 1f - right : right;
                }
                // The pelvis unloads the struck leg before footwork lifts it.
                // Knees are solved against the real soles, never rotated after IK.
                float healthySide = InjuredLegSide == 0 ? 1f : -1f;
                projectileTargetShift = frame.right * (healthySide * Mathf.Lerp(.035f, .075f, load));
                projectileTargetDrop = Mathf.Lerp(.055f, .105f, load);
                AddProjectileJoint(1, frame.forward * (-healthySide * .14f) + bend * .10f);
                AddProjectileJoint(2, frame.forward * (healthySide * .07f) + frame.right * .10f);
                AddProjectileJoint(4, -bend * .08f);
            }
            projectileAge = 0f;
            EvaluateProjectileReaction();
        }

        private void AddProjectileJoint(int index, Vector3 radians)
        {
            float limit = index == 5 || index == 8 ? .52f : index == 6 || index == 9 ? .45f :
                index == 7 || index == 10 ? .30f : index >= 3 ? .22f : .48f;
            projectileTarget[index] = Vector3.ClampMagnitude(AnatomicalRotation(index, radians) + projectileFrom[index] * .18f, limit);
        }

        private void AdvanceProjectileReaction(float seconds)
        {
            if (!ProjectileReactionActive) return;
            projectileAge = Mathf.Min(ProjectileTailSeconds, projectileAge + seconds);
            EvaluateProjectileReaction();
        }

        private void EvaluateProjectileReaction()
        {
            if (projectileAge < ProjectileSnapSeconds)
            {
                float snap = Mathf.SmoothStep(0f, 1f, projectileAge / ProjectileSnapSeconds);
                for (int i = 0; i < projectilePose.Length; i++) projectilePose[i] = Vector3.Lerp(projectileFrom[i], projectileTarget[i], snap);
                projectileShift = Vector3.Lerp(projectileFromShift, projectileTargetShift, snap);
                projectileDrop = Mathf.Lerp(projectileFromDrop, projectileTargetDrop, snap);
                ProjectileReactionAmount = snap;
                return;
            }
            // Sharp loss of pose, a readable short hold, then a slower recovery.
            float envelope = projectileAge < .28f ? Mathf.Lerp(1f, .42f,
                Mathf.SmoothStep(0f, 1f, (projectileAge - ProjectileSnapSeconds) / (.28f - ProjectileSnapSeconds))) :
                .42f * (1f - Mathf.SmoothStep(0f, 1f, (projectileAge - .28f) / (ProjectileTailSeconds - .28f)));
            for (int i = 0; i < projectilePose.Length; i++) projectilePose[i] = projectileTarget[i] * envelope;
            projectileShift = projectileTargetShift * envelope; projectileDrop = projectileTargetDrop * envelope;
            ProjectileReactionAmount = envelope;
        }

        private void ApplyProjectileReaction(float torsoScale)
        {
            if (!ProjectileReactionActive) return;
            for (int i = 0; i < projectilePose.Length; i++)
            {
                Vector3 radians = projectilePose[i] * (i <= 2 ? torsoScale : 1f);
                if (i == 6 || i == 9)
                {
                    // Keep the elbow inside its bend range even when the
                    // interrupted action already had a nearly straight arm.
                    Vector3 upper = bones[i - 1].position - bones[i].position;
                    Vector3 lower = bones[i + 1].position - bones[i].position;
                    for (int attempt = 0; attempt < 6 && radians.sqrMagnitude > .000001f; attempt++)
                    {
                        Quaternion turn = Quaternion.AngleAxis(radians.magnitude * Mathf.Rad2Deg, radians.normalized);
                        float angle = Vector3.Angle(upper, turn * lower);
                        if (angle >= 25f && angle <= 176f) break;
                        radians = attempt == 5 ? Vector3.zero : radians * .5f;
                    }
                }
                Rotate(i, radians);
            }
        }

        private void ResetProjectileReaction()
        {
            Array.Clear(projectileFrom, 0, projectileFrom.Length);
            Array.Clear(projectileTarget, 0, projectileTarget.Length);
            Array.Clear(projectilePose, 0, projectilePose.Length);
            projectileAge = ProjectileTailSeconds; ProjectileReactionAmount = 0f; InjuredLegSide = -1;
            projectileDrop = projectileFromDrop = projectileTargetDrop = 0f;
            projectileShift = projectileFromShift = projectileTargetShift = Vector3.zero;
            ProjectileReactionPart = default;
        }
    }
}
