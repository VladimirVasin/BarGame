using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One duel-clock momentum and support response for both rigs. Applying a pose is read-only.
    /// Momentum is in N s, velocities in metres/radians per second; HP never scales this response.</summary>
    public sealed class CombatImpactMotion
    {
        public const float BodyMass = 70f;
        private readonly Transform[] bones;
        private readonly Vector3[] basePositions, localRotation, localVelocity;
        private readonly Quaternion[] baseRotations;
        private bool applied;
        private Vector3 velocity, rotation, angularVelocity;
        private float age = 10f, overload, drop, dropVelocity, legWeakness;
        private float supportReach = .62f;
        private int struckBone = 2;
        public Vector3 Velocity => velocity;
        public Vector3 AngularVelocity => angularVelocity;
        public Vector3 Rotation => rotation;
        public Vector3 CaptureOffset { get; private set; }
        public float BalanceLoad { get; private set; }
        public float Age => age;
        public bool WantsKnockdown => overload >= .09f;
        public bool IsActive => age < .9f || velocity.sqrMagnitude > .0025f || rotation.sqrMagnitude > .0001f;
        public float ReactionAmount => Mathf.Clamp01(Mathf.Max(rotation.magnitude * 2f, velocity.magnitude / 2f));
        public MeleeBodyRegion StruckRegion { get; private set; }
        public CombatImpact LastImpact { get; private set; }
        public Vector3 CentreOfMass => bones[0].position + Vector3.up * .20f;

        public CombatImpactMotion(Transform rig, Transform actor)
        {
            string[] names = { "pelvis", "spine", "chest", "neck", "head", "upper_arm.L", "forearm.L",
                "hand.L", "upper_arm.R", "forearm.R", "hand.R", "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R" };
            var found = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in rig.GetComponentsInChildren<Transform>(true)) found[bone.name] = bone;
            bones = new Transform[names.Length];
            for (int i = 0; i < names.Length; i++)
                if (!found.TryGetValue(names[i], out bones[i])) throw new InvalidOperationException("Impact rig lacks " + names[i]);
            basePositions = new Vector3[bones.Length]; baseRotations = new Quaternion[bones.Length];
            localRotation = new Vector3[bones.Length]; localVelocity = new Vector3[bones.Length];
        }

        public static Vector3 ResolveImpulse(Vector3 direction, float power, float weaponSpeed, MeleeHitResult result)
        {
            if (!Finite(direction) || direction.sqrMagnitude < .000001f || result == MeleeHitResult.Ignored || result == MeleeHitResult.Parried)
                return Vector3.zero;
            // Sampling density or a first overlapping sample cannot amplify the weapon.
            float speed = weaponSpeed > .01f && float.IsFinite(weaponSpeed) ? Mathf.Clamp(weaponSpeed / 5f, .8f, 1.15f) : 1f;
            float momentum = Mathf.Lerp(100f, 205f, Mathf.Clamp01(power)) * speed;
            if (result == MeleeHitResult.Blocked) momentum *= .23f;
            else if (result == MeleeHitResult.GuardBroken) momentum *= .85f;
            return direction.normalized * momentum;
        }

        public void Hit(CombatImpact impact, Vector3 carryVelocity, float stamina01, float intoxication01, bool transferringFoot)
        {
            if (!Finite(impact.Impulse) || impact.Impulse.sqrMagnitude < .0001f) return;
            bool fresh = !IsActive;
            LastImpact = impact; age = 0f;
            StruckRegion = impact.Location.Region;
            struckBone = ResponseBone(BoneIndex(impact.Part));
            Vector3 planar = Vector3.ProjectOnPlane(impact.Impulse, Vector3.up);
            if (fresh && Finite(carryVelocity)) velocity = Vector3.ProjectOnPlane(carryVelocity, Vector3.up) * .35f;
            velocity = Vector3.ClampMagnitude(velocity + planar / BodyMass, 4.2f);
            Vector3 arm = Vector3.ClampMagnitude(impact.Point - CentreOfMass, 1.1f);
            Vector3 torque = Vector3.Cross(arm, impact.Impulse) / 32f;
            // A centred strike also rocks the body about its loaded boot.
            torque += Vector3.Cross(Vector3.up, planar) / 115f;
            angularVelocity = Vector3.ClampMagnitude(angularVelocity + torque, 5.5f);
            // A hand/forearm contact moves its shoulder; the elbow and wrist keep
            // their authored bend. Planted legs belong to footwork IK throughout.
            // Exact contact still supplies the whole-body torque and support loss.
            if (struckBone >= 0)
            {
                Vector3 localAxis = Vector3.Cross(Vector3.up, impact.Impulse.normalized);
                Vector3 kick = localAxis * Mathf.Clamp(impact.Impulse.magnitude / 38f, .3f, 4.5f) + torque * .20f;
                localVelocity[struckBone] = Vector3.ClampMagnitude(localVelocity[struckBone] + AnatomicalRotation(struckBone, kick), 6f);
            }
            bool leg = StruckRegion == MeleeBodyRegion.LeftLeg || StruckRegion == MeleeBodyRegion.RightLeg;
            Vector3 across = Vector3.ProjectOnPlane(bones[16].position - bones[13].position, Vector3.up);
            Vector3 massOffset = Vector3.ProjectOnPlane(CentreOfMass - bones[13].position, Vector3.up);
            float rightLoad = across.sqrMagnitude > .001f ? Mathf.Clamp01(Vector3.Dot(massOffset, across) / across.sqrMagnitude) : .5f;
            float struckLoad = StruckRegion == MeleeBodyRegion.LeftLeg ? 1f - rightLoad : rightLoad;
            legWeakness = Mathf.Max(legWeakness, leg ? .4f + .6f * struckLoad : 0f);
            dropVelocity += leg ? -.5f - struckLoad * .9f : Mathf.Min(0f, impact.Impulse.y / BodyMass) * .25f;
            float stance = .13f + Mathf.Abs(Vector3.Dot(across, planar.normalized)) * .5f;
            supportReach = stance + Mathf.Lerp(.20f, .32f, Mathf.Clamp01(stamina01)) - Mathf.Clamp01(intoxication01) * .10f
                - (transferringFoot ? .13f : 0f) - (leg ? .07f + struckLoad * .15f : 0f);
            MeasureBalance();
        }

        public void Push(Vector3 momentum)
        {
            if (!Finite(momentum)) return;
            velocity = Vector3.ClampMagnitude(velocity + Vector3.ProjectOnPlane(momentum, Vector3.up) / BodyMass, 4.2f);
        }

        public Vector3 Advance(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f) return Vector3.zero;
            Vector3 displacement = Vector3.zero;
            float remaining = Mathf.Min(seconds, .25f);
            while (remaining > .000001f)
            {
                float dt = Mathf.Min(remaining, 1f / 120f); remaining -= dt; age += dt;
                float decay = Mathf.Exp(-4.2f * dt);
                displacement += velocity * ((1f - decay) / 4.2f); velocity *= decay;
                angularVelocity += (-rotation * 58f - angularVelocity * 10f) * dt;
                rotation = Vector3.ClampMagnitude(rotation + angularVelocity * dt, .72f);
                for (int i = 0; i < bones.Length; i++)
                {
                    localVelocity[i] += (-localRotation[i] * 75f - localVelocity[i] * 10f) * dt;
                    float limit = i == 5 || i == 8 ? .26f : i >= 3 ? .16f : .20f;
                    localRotation[i] = Vector3.ClampMagnitude(localRotation[i] + localVelocity[i] * dt, limit);
                }
                dropVelocity += (-drop * 65f - dropVelocity * 11f) * dt;
                drop = Mathf.Clamp(drop + dropVelocity * dt, -.14f, .025f);
                legWeakness = Mathf.MoveTowards(legWeakness, 0f, dt * 1.6f);
                MeasureBalance();
                overload = BalanceLoad > 1f && age < .55f ? overload + dt : Mathf.Max(0f, overload - dt * 2f);
            }
            return displacement;
        }

        private void MeasureBalance()
        {
            // Capture point of a roughly metre-high centre of mass. The extra reach
            // is the available emergency step, reduced by an injured/transferring leg.
            CaptureOffset = velocity / 3.2f + Vector3.Cross(rotation, Vector3.up) * .85f;
            BalanceLoad = CaptureOffset.magnitude / Mathf.Max(.28f, supportReach);
        }

        public void AcceptDisplacement(Vector3 requested, Vector3 achieved)
        {
            Vector3 refused = requested - achieved; refused.y = 0f;
            if (refused.magnitude < .002f || velocity.sqrMagnitude < .001f) return;
            Vector3 normal = refused.normalized;
            velocity -= normal * Mathf.Max(0f, Vector3.Dot(velocity, normal));
            // A wall takes the blocked momentum. Never bank refused distance for a later frame.
        }

        public void Apply()
        {
            Restore();
            if (!IsActive) return;
            for (int i = 0; i < bones.Length; i++) { basePositions[i] = bones[i].localPosition; baseRotations[i] = bones[i].localRotation; }
            applied = true;
            bones[0].position += Vector3.up * Mathf.Max(-.16f,
                drop - .055f * Mathf.Clamp01(BalanceLoad) - .055f * legWeakness);
            // These are successive joints in one spine, so their bends add.
            // Bound the total added torso bend, including localized contacts.
            float torsoAmount = rotation.magnitude + localRotation[0].magnitude +
                localRotation[1].magnitude + localRotation[2].magnitude;
            float torsoScale = torsoAmount > .55f ? .55f / torsoAmount : 1f;
            Rotate(0, rotation * (.25f * torsoScale));
            Rotate(1, rotation * (.30f * torsoScale));
            Rotate(2, rotation * (.45f * torsoScale));
            Rotate(3, rotation * -.10f);
            Rotate(4, rotation * .17f);
            for (int i = 0; i < bones.Length; i++) Rotate(i,
                AnatomicalRotation(i, localRotation[i]) * (i <= 2 ? torsoScale : 1f));
        }

        private static int ResponseBone(int contactBone) => contactBone switch
        {
            6 or 7 => 5,
            9 or 10 => 8,
            >= 11 => -1,
            _ => contactBone
        };

        private Vector3 AnatomicalRotation(int index, Vector3 radians)
        {
            // Remove twist about the actual segment, including when the actor is
            // turned or the arm is raised. Never infer a hinge from imported axes.
            int next = index switch { 0 => 1, 1 => 2, 2 => 3, 3 => 4, 5 => 6, 8 => 9, _ => -1 };
            Vector3 axis = next >= 0 ? bones[next].position - bones[index].position :
                index == 4 ? bones[4].position - bones[3].position : Vector3.zero;
            return axis.sqrMagnitude > .00001f ? Vector3.ProjectOnPlane(radians, axis.normalized) : Vector3.zero;
        }

        private void Rotate(int i, Vector3 radians)
        {
            if (radians.sqrMagnitude > .0000001f)
                bones[i].rotation = Quaternion.AngleAxis(radians.magnitude * Mathf.Rad2Deg, radians.normalized) * bones[i].rotation;
        }

        public void Restore()
        {
            if (!applied) return;
            for (int i = 0; i < bones.Length; i++) if (bones[i] != null)
            { bones[i].localPosition = basePositions[i]; bones[i].localRotation = baseRotations[i]; }
            applied = false;
        }
        public void Forget() => applied = false;
        public void Reset()
        {
            Restore(); velocity = rotation = angularVelocity = Vector3.zero;
            Array.Clear(localRotation, 0, localRotation.Length); Array.Clear(localVelocity, 0, localVelocity.Length);
            age = 10f; overload = drop = dropVelocity = legWeakness = BalanceLoad = 0f; CaptureOffset = Vector3.zero;
            LastImpact = default; StruckRegion = default; supportReach = .62f; struckBone = 2;
        }
        private static int BoneIndex(Player3DAnatomicalPart part) => part switch
        {
            Player3DAnatomicalPart.Pelvis => 0, Player3DAnatomicalPart.LowerTorso => 1, Player3DAnatomicalPart.Neck => 3,
            Player3DAnatomicalPart.Head => 4, Player3DAnatomicalPart.LeftUpperArm => 5, Player3DAnatomicalPart.LeftForearm => 6,
            Player3DAnatomicalPart.LeftHand => 7, Player3DAnatomicalPart.RightUpperArm => 8, Player3DAnatomicalPart.RightForearm => 9,
            Player3DAnatomicalPart.RightHand => 10, Player3DAnatomicalPart.LeftThigh => 11, Player3DAnatomicalPart.LeftShin => 12,
            Player3DAnatomicalPart.LeftFoot => 13, Player3DAnatomicalPart.RightThigh => 14, Player3DAnatomicalPart.RightShin => 15,
            Player3DAnatomicalPart.RightFoot => 16, _ => 2
        };
        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
